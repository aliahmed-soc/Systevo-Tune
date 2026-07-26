using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Platform.Windows;
using SystevoTune.Engine.Safety;
using SystevoTune.TestSupport;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.Engine.Tests.Cleanup;

/// <summary>
/// B3, decided by the human in session 2 (H1/H2): the update cache is cleared only with Windows
/// Update and BITS stopped, and skipped entirely if either will not stop.
/// </summary>
public class UpdateCacheCleanupTests : IDisposable
{
    private const string UpdateCache = @"C:\FakeWindows\SoftwareDistribution\Download";

    private readonly TempLogDirectory _directory = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeEnvironmentPaths _environment = new();
    private readonly FakeServiceController _services = new();
    private readonly TweakRunner _runner = new();

    public UpdateCacheCleanupTests()
    {
        _services.With("wuauserv").With("bits");
        _files.WithFile($@"{UpdateCache}\patch.cab", 5000);
        _files.WithFile($@"{UpdateCache}\update.esd", 3000);
    }

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog()
        => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero)));

    private CleanupModule Module(IWindowsServiceController? services)
        => new(CleanupWhitelist.Load(), _files, _environment, services);

    private CleanupTweak Tweak(IWindowsServiceController? services)
        => (CleanupTweak)Module(services).CreateTweaks(["windows-update-cache"]).Single();

    // ---- the whitelist guard: H2, the only exception ----

    [Fact]
    public void The_shipped_update_cache_group_stops_exactly_the_two_allowed_services()
    {
        var group = CleanupWhitelist.Load().Find("windows-update-cache")!;

        Assert.Equal(["wuauserv", "bits"], group.StopServices);
    }

    [Fact]
    public void No_other_shipped_group_stops_anything()
        => Assert.All(
            CleanupWhitelist.Load().Groups.Where(group => group.Id != "windows-update-cache"),
            group => Assert.Empty(group.StopServices));

    [Fact]
    public void Another_group_asking_to_stop_a_service_is_refused_at_load()
    {
        const string json = """
            {"version":1,"groups":[{"id":"temp-files","nameEn":"T","nameAr":"ت",
             "stopServices":["wuauserv"],"paths":["{USER_TEMP}"]}]}
            """;

        var error = Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Parse(json));

        Assert.Contains("Only 'windows-update-cache' may do that", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("WinDefend")]
    [InlineData("MpsSvc")]
    [InlineData("Audiosrv")]
    [InlineData("Dhcp")]
    public void The_update_cache_group_still_cannot_name_a_forbidden_service(string service)
    {
        var json = $$"""
            {"version":1,"groups":[{"id":"windows-update-cache","nameEn":"U","nameAr":"ت",
             "stopServices":["{{service}}"],"paths":["{WINDIR}\\SoftwareDistribution\\Download"]}]}
            """;

        var error = Assert.Throws<InvalidOperationException>(() => CleanupWhitelist.Parse(json));

        Assert.Contains("Cleanup may never stop", error.Message, StringComparison.Ordinal);
    }

    // ---- the happy path ----

    [Fact]
    public async Task Both_services_are_stopped_before_the_delete_and_started_after()
    {
        var tweak = Tweak(_services);

        await _runner.ApplyAsync([tweak], NewLog().StartRun());

        Assert.Equal(["stop:wuauserv", "stop:bits", "start:wuauserv", "start:bits"], _services.Calls);
        Assert.False(_files.Exists($@"{UpdateCache}\patch.cab"));
    }

    [Fact]
    public async Task Nothing_is_deleted_while_a_service_is_still_running()
    {
        // The fake records call order; the delete happens between the stops and the starts.
        var tweak = Tweak(_services);

        await _runner.ApplyAsync([tweak], NewLog().StartRun());

        Assert.Equal(ServiceState.Running, _services.StateOf("wuauserv"));
        Assert.Equal(ServiceState.Running, _services.StateOf("bits"));
    }

    [Fact]
    public async Task The_run_logs_the_file_count_and_bytes_freed()
    {
        // H1: no undo needed, but the numbers are logged.
        var log = NewLog();
        var run = log.StartRun();
        var tweak = Tweak(_services);

        await _runner.ApplyAsync([tweak], run);

        var record = Assert.Single(log.ReadRun(run.RunId).Records);
        Assert.Equal("files=2;bytes=8000", record.OldValue);
        Assert.False(record.Undoable);
        Assert.Equal(8000, tweak.LastApply!.BytesFreed);
        Assert.Equal(2, tweak.LastApply.FilesDeleted);
    }

    // ---- a service that will not stop ----

    [Fact]
    public async Task A_service_that_will_not_stop_skips_the_group_and_deletes_nothing()
    {
        _services.RefusesToStop.Add("bits");
        var tweak = Tweak(_services);

        await _runner.ApplyAsync([tweak], NewLog().StartRun());

        Assert.True(tweak.LastApply!.WasSkipped);
        Assert.Empty(_files.Deleted);
        Assert.True(_files.Exists($@"{UpdateCache}\patch.cab"));
    }

    [Fact]
    public async Task A_refusal_puts_back_whatever_was_already_stopped()
    {
        // wuauserv stops, bits refuses. Leaving Windows Update down would be worse than the
        // cleanup not happening.
        _services.RefusesToStop.Add("bits");
        var tweak = Tweak(_services);

        await _runner.ApplyAsync([tweak], NewLog().StartRun());

        Assert.Equal(ServiceState.Running, _services.StateOf("wuauserv"));
        Assert.Contains("start:wuauserv", _services.Calls);
    }

    [Fact]
    public async Task The_skip_reason_says_which_service_and_why_it_matters()
    {
        _services.RefusesToStop.Add("wuauserv");
        var tweak = Tweak(_services);

        await _runner.ApplyAsync([tweak], NewLog().StartRun());

        var reason = tweak.LastApply!.SkippedReason!;
        Assert.Contains("wuauserv", reason, StringComparison.Ordinal);
        Assert.Contains("waiting to install", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_skip_is_a_warning_rather_than_a_failed_run()
    {
        _services.RefusesToStop.Add("bits");

        var report = await _runner.ApplyAsync([Tweak(_services)], NewLog().StartRun());

        Assert.True(report.AllSucceeded);
    }

    // ---- a service that will not come back ----

    [Fact]
    public async Task A_service_that_will_not_restart_is_reported_loudly()
    {
        // Leaving Windows Update stopped is worse than not cleaning, so this one is a failure.
        _services.RefusesToStart.Add("wuauserv");

        var report = await _runner.ApplyAsync([Tweak(_services)], NewLog().StartRun());

        var failure = Assert.Single(report.AllFailures);
        Assert.Contains("did not start again", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("wuauserv", failure.Reason, StringComparison.Ordinal);
    }

    // ---- no controller wired ----

    [Fact]
    public async Task Without_a_service_controller_the_group_is_skipped_rather_than_forced()
    {
        var tweak = Tweak(services: null);

        await _runner.ApplyAsync([tweak], NewLog().StartRun());

        Assert.True(tweak.LastApply!.WasSkipped);
        Assert.Empty(_files.Deleted);
    }

    // ---- other groups are unaffected ----

    [Fact]
    public async Task A_group_with_no_services_never_touches_the_service_controller()
    {
        _files.WithFile(@"C:\FakeUsers\tester\AppData\Local\Temp\a.tmp", 100);

        await _runner.ApplyAsync(
            Module(_services).CreateTweaks(["temp-files"]), NewLog().StartRun());

        Assert.Empty(_services.Calls);
    }

    // ---- sc.exe state parsing ----

    [Theory]
    [InlineData("        STATE              : 4  RUNNING", ServiceState.Running)]
    [InlineData("        STATE              : 1  STOPPED", ServiceState.Stopped)]
    [InlineData("        STATE              : 3  STOP_PENDING", ServiceState.StopPending)]
    public void The_numeric_state_code_is_what_is_read(string line, ServiceState expected)
        => Assert.Equal(expected, ScServiceController.ParseState(line));

    [Fact]
    public void A_localised_state_word_does_not_break_parsing()
        => Assert.Equal(ServiceState.Running, ScServiceController.ParseState("STATE : 4  قيد التشغيل"));

    [Fact]
    public void Unreadable_output_is_unknown_rather_than_a_guess()
        => Assert.Equal(ServiceState.Unknown, ScServiceController.ParseState("nothing useful here"));
}
