using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tests.Fakes;
using SystevoTune.Engine.Tests.Safety;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.Engine.Tests.Profiles;

/// <summary>Doc 5.6: Windows updates reset tweaks, so the last profile can be run again.</summary>
public class ReapplyTests : IDisposable
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid High = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private readonly TempLogDirectory _directory = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeRegistryService _registry = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeEnvironmentPaths _environment = new();
    private readonly FakePowerPlanService _powerPlans = new();
    private readonly FakeBatteryStatus _battery = new();
    private readonly ProfileCatalog _profiles = ProfileCatalog.Load();
    private readonly RegistryTweakCatalog _registryTweaks = RegistryTweakCatalog.Load();
    private readonly ChangeLog _log;

    public ReapplyTests()
    {
        _log = new ChangeLog(_directory.Path, _clock);
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
    }

    public void Dispose() => _directory.Dispose();

    private ProfileApplier Applier() => new(
        new ProfileBuilder(
            new CleanupModule(CleanupWhitelist.Load(), _files, _environment),
            _registryTweaks,
            _registry,
            _powerPlans,
            PowerPlanCatalog.Load(),
            _battery),
        new TweakRunner());

    private ReapplyService Service() => new(_log, _profiles);

    private UndoEngine Undo() => new(_log,
        [new RegistryUndoHandler(_registry), new PowerPlanUndoHandler(_powerPlans)]);

    // ---- finding the last profile ----

    [Fact]
    public void An_empty_log_has_nothing_to_reapply()
        => Assert.Null(Service().FindLast());

    [Fact]
    public async Task The_profile_a_run_applied_is_remembered()
    {
        await Applier().ApplyAsync(_profiles.Find("gaming")!, _log.StartRun());

        var target = Service().FindLast();

        Assert.Equal("gaming", target!.ProfileId);
        Assert.True(target.ChangeCount > 0);
    }

    [Fact]
    public async Task The_most_recent_profile_wins()
    {
        await Applier().ApplyAsync(_profiles.Find("gaming")!, _log.StartRun());
        _clock.Advance(TimeSpan.FromMinutes(10));
        await Applier().ApplyAsync(_profiles.Find("work")!, _log.StartRun());

        Assert.Equal("work", Service().FindLast()!.ProfileId);
    }

    [Fact]
    public async Task A_run_that_applied_no_profile_is_skipped()
    {
        // A one-off tweak run, then a profile run before it.
        await Applier().ApplyAsync(_profiles.Find("work")!, _log.StartRun());
        _clock.Advance(TimeSpan.FromMinutes(5));
        var loose = _log.StartRun();
        loose.RecordChange("Registry", "SetValue", "HKCU\\Software\\X::Y", "Dword:0", "Dword:1");

        Assert.Equal("work", Service().FindLast()!.ProfileId);
    }

    [Fact]
    public async Task A_profile_that_no_longer_exists_is_skipped_rather_than_crashing()
    {
        var run = _log.StartRun();
        run.RecordProfile("profile-from-an-older-build");

        Assert.Null(Service().FindLast());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Find_last_profile_hands_back_something_runnable()
    {
        await Applier().ApplyAsync(_profiles.Find("gaming")!, _log.StartRun());

        Assert.Equal("gaming", Service().FindLastProfile()!.Id);
    }

    // ---- the marker is metadata, not a change ----

    [Fact]
    public async Task The_marker_is_not_counted_as_a_change()
    {
        await Applier().ApplyAsync(_profiles.Find("work")!, _log.StartRun());
        var run = _log.ReadAllRuns()[0];

        Assert.Equal(run.Records.Count - 1, run.Changes.Count);
        Assert.True(run.Records[0].IsMetadata);
    }

    [Fact]
    public async Task Undo_ignores_the_marker_instead_of_calling_it_permanent()
    {
        // Listing "gaming" as a change that cannot be undone would be nonsense.
        await Applier().ApplyAsync(_profiles.Find("gaming")!, _log.StartRun());

        var undo = await Undo().UndoAllAsync();

        Assert.DoesNotContain(undo.Permanent, record => record.IsMetadata);
        Assert.DoesNotContain(undo.Undone, record => record.IsMetadata);
        Assert.Empty(undo.Failures);
    }

    // ---- re-applying ----

    [Fact]
    public async Task Re_applying_after_windows_reset_a_tweak_writes_only_what_was_lost()
    {
        var profile = _profiles.Find("gaming")!;
        await Applier().ApplyAsync(profile, _log.StartRun());
        var visualEffects = _registryTweaks.Find("visual-effects.performance")!.Values[0].ToRef();

        // Windows update puts one value back the way it was.
        _registry.SetValue(visualEffects, RegistryValue.Dword(1));
        _registry.Writes.Clear();

        _clock.Advance(TimeSpan.FromDays(1));
        var again = await Applier().ApplyAsync(Service().FindLastProfile()!, _log.StartRun());

        Assert.True(again.Report.AllSucceeded);
        Assert.Equal(RegistryValue.Dword(2), _registry.GetValue(visualEffects));
        // Everything else was already correct, so nothing else was rewritten.
        Assert.Single(_registry.Writes);
    }

    [Fact]
    public async Task Re_applying_makes_a_fresh_run_with_its_own_undo_path()
    {
        var profile = _profiles.Find("gaming")!;
        await Applier().ApplyAsync(profile, _log.StartRun());
        _clock.Advance(TimeSpan.FromDays(1));

        await Applier().ApplyAsync(Service().FindLastProfile()!, _log.StartRun());

        Assert.Equal(2, _log.ListRunIds().Count);
        Assert.All(_log.ReadAllRuns(), run => Assert.Equal("gaming", run.ProfileId));
    }

    [Fact]
    public async Task Undo_after_a_re_apply_still_lands_on_the_original_values()
    {
        // Two runs of the same profile, then Undo All. The oldest record has the last word.
        var visualEffects = _registryTweaks.Find("visual-effects.performance")!.Values[0].ToRef();
        _registry.With(visualEffects, RegistryValue.Dword(3));
        var profile = _profiles.Find("gaming")!;
        await Applier().ApplyAsync(profile, _log.StartRun());
        _clock.Advance(TimeSpan.FromDays(1));
        _registry.SetValue(visualEffects, RegistryValue.Dword(1));
        await Applier().ApplyAsync(profile, _log.StartRun());

        var undo = await Undo().UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(RegistryValue.Dword(3), _registry.GetValue(visualEffects));
        Assert.Equal(Balanced, _powerPlans.Active);
    }

    [Fact]
    public async Task The_tweaks_that_ran_come_back_so_per_tweak_detail_survives()
    {
        _files.WithFile(@"C:\FakeUsers\tester\AppData\Local\Temp\a.tmp", 2048);

        var result = await Applier().ApplyAsync(_profiles.Find("work")!, _log.StartRun());

        var cleanup = result.Tweaks.OfType<CleanupTweak>().First(tweak => tweak.Id == "cleanup.temp-files");
        Assert.Equal(2048, cleanup.LastApply!.BytesFreed);
    }
}
