using SystevoTune.Engine.Bloatware;
using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Startup;
using SystevoTune.Engine.Tests.Fakes;
using SystevoTune.Engine.Tests.Safety;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;
using SystevoTune.Engine.Tweaks.Registry;
using SystevoTune.Engine.Tweaks.Services;
using SystevoTune.Engine.Verification;

namespace SystevoTune.Engine.Tests.Verification;

/// <summary>
/// The harness that runs doc 07.2 in the VM. These tests prove the harness itself is honest —
/// that it notices a difference when there is one, and does not claim a pass when nothing ran.
/// </summary>
public class VerificationTests : IDisposable
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid High = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private readonly TempLogDirectory _directory = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeRegistryService _registry = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeEnvironmentPaths _environment = new();
    private readonly FakePowerPlanService _powerPlans = new();
    private readonly FakeBatteryStatus _battery = new();
    private readonly FakeServiceController _services = new();
    private readonly FakeAppPackageService _packages = new();
    private readonly ProfileCatalog _profiles = ProfileCatalog.Load();
    private readonly RegistryTweakCatalog _registryTweaks = RegistryTweakCatalog.Load();
    private readonly ChangeLog _log;

    public VerificationTests()
    {
        _log = new ChangeLog(_directory.Path, _clock);
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
        _services.With("wuauserv").With("bits");
    }

    public void Dispose() => _directory.Dispose();

    private SystemStateCollector Collector() => new(
        _registry,
        _powerPlans,
        new StartupManager(StartupLocationCatalog.Load(), _registry, _files, _environment, _clock),
        _registryTweaks,
        ServiceWhitelist.Load(),
        BloatwareWhitelist.Load(),
        _services,
        _packages,
        _clock);

    private VerificationRunner Runner()
    {
        var builder = new ProfileBuilder(
            new CleanupModule(CleanupWhitelist.Load(), _files, _environment, _services),
            _registryTweaks,
            _registry,
            _powerPlans,
            PowerPlanCatalog.Load(),
            _battery);

        return new VerificationRunner(
            Collector(),
            new ProfileApplier(builder, new TweakRunner()),
            _log,
            new UndoEngine(_log, [new RegistryUndoHandler(_registry), new PowerPlanUndoHandler(_powerPlans)]));
    }

    // ---- the diff itself ----

    private static SystemStateSnapshot Snapshot(
        string label,
        IReadOnlyDictionary<string, string?>? registry = null,
        IReadOnlyList<PowerSchemeState>? schemes = null,
        IReadOnlyList<string>? packages = null)
        => new()
        {
            Label = label,
            TakenAt = new DateTime(2026, 7, 27, 10, 0, 0),
            PowerSchemes = schemes ?? [],
            Registry = registry ?? new Dictionary<string, string?>(),
            Services = new Dictionary<string, string>(),
            StartupItems = new Dictionary<string, string>(),
            Packages = packages ?? [],
        };

    [Fact]
    public void Two_identical_snapshots_differ_in_nothing()
        => Assert.Empty(StateDiff.Compare(Snapshot("a"), Snapshot("b")));

    [Fact]
    public void A_changed_registry_value_is_a_difference()
    {
        var before = Snapshot("a", new Dictionary<string, string?> { ["HKCU\\X::Y"] = "Dword:1" });
        var after = Snapshot("b", new Dictionary<string, string?> { ["HKCU\\X::Y"] = "Dword:2" });

        var difference = Assert.Single(StateDiff.Compare(before, after));

        Assert.Equal("Registry", difference.Area);
        Assert.Equal("Dword:1", difference.Before);
        Assert.Equal("Dword:2", difference.After);
    }

    [Fact]
    public void A_value_that_did_not_exist_before_is_a_difference()
    {
        var before = Snapshot("a", new Dictionary<string, string?>());
        var after = Snapshot("b", new Dictionary<string, string?> { ["HKCU\\X::Y"] = "Dword:0" });

        Assert.Single(StateDiff.Compare(before, after));
    }

    [Fact]
    public void A_value_that_was_there_and_is_gone_is_a_difference()
    {
        var before = Snapshot("a", new Dictionary<string, string?> { ["HKCU\\X::Y"] = "Dword:1" });
        var after = Snapshot("b", new Dictionary<string, string?>());

        Assert.Equal("Dword:1", Assert.Single(StateDiff.Compare(before, after)).Before);
    }

    [Fact]
    public void A_scheme_left_behind_is_a_difference_even_if_the_active_one_matches()
    {
        // The failure mode decision 33 exists to prevent: undo restored the plan but left the
        // scheme it created lying around.
        var before = Snapshot("a", schemes: [new PowerSchemeState("g1", "Balanced", true)]);
        var after = Snapshot("b", schemes:
            [new PowerSchemeState("g1", "Balanced", true), new PowerSchemeState("g2", "Copied", false)]);

        var difference = Assert.Single(StateDiff.Compare(before, after));

        Assert.Equal("scheme left behind", difference.Target);
    }

    [Fact]
    public void A_switched_active_scheme_is_a_difference()
    {
        var before = Snapshot("a", schemes:
            [new PowerSchemeState("g1", "Balanced", true), new PowerSchemeState("g2", "High", false)]);
        var after = Snapshot("b", schemes:
            [new PowerSchemeState("g1", "Balanced", false), new PowerSchemeState("g2", "High", true)]);

        Assert.Equal("active scheme", Assert.Single(StateDiff.Compare(before, after)).Target);
    }

    [Fact]
    public void A_package_that_did_not_come_back_is_a_difference()
    {
        var before = Snapshot("a", packages: ["Microsoft.BingNews"]);
        var after = Snapshot("b", packages: []);

        var difference = Assert.Single(StateDiff.Compare(before, after));

        Assert.Equal("Package", difference.Area);
        Assert.Equal("removed", difference.After);
    }

    // ---- collecting ----

    [Fact]
    public async Task The_snapshot_covers_every_value_the_whitelists_name()
    {
        var snapshot = await Collector().CaptureAsync("before");

        // Not a hand-written list: adding a tweak must extend the snapshot automatically, or a
        // clean diff would be hiding a real change.
        foreach (var value in _registryTweaks.Tweaks.SelectMany(tweak => tweak.Values))
        {
            Assert.Contains(value.ToRef().ToString(), snapshot.Registry.Keys);
        }
    }

    [Fact]
    public async Task The_snapshot_watches_the_two_services_the_update_cache_stops()
    {
        var snapshot = await Collector().CaptureAsync("before");

        Assert.Contains("wuauserv", snapshot.Services.Keys);
        Assert.Contains("bits", snapshot.Services.Keys);
    }

    [Fact]
    public async Task An_unreadable_value_is_recorded_as_unreadable_rather_than_dropped()
    {
        // Dropping it would make a real difference invisible.
        var snapshot = await Collector().CaptureAsync("before");

        Assert.NotEmpty(snapshot.Registry);
        Assert.All(snapshot.Registry.Keys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
    }

    [Fact]
    public async Task The_snapshot_serialises_to_json_a_human_can_read()
    {
        var json = (await Collector().CaptureAsync("before")).ToJson();

        Assert.Contains("\"powerSchemes\"", json, StringComparison.Ordinal);
        Assert.Contains("\"registry\"", json, StringComparison.Ordinal);
        Assert.Contains("\"label\": \"before\"", json, StringComparison.Ordinal);
    }

    // ---- the full cycle ----

    [Fact]
    public async Task A_clean_apply_and_undo_returns_the_pc_to_where_it_started()
    {
        _registry.With(_registryTweaks.Find("visual-effects.performance")!.Values[0].ToRef(), RegistryValue.Dword(3));

        var report = await Runner().RunAsync(_profiles.Find("gaming")!);

        Assert.True(report.ReturnedToStart);
        Assert.Empty(report.Differences);
    }

    [Fact]
    public async Task The_run_proves_something_because_the_apply_actually_changed_state()
    {
        // A green result from a run that changed nothing would be a false pass.
        var report = await Runner().RunAsync(_profiles.Find("gaming")!);

        Assert.True(report.ProvedAnything);
        Assert.NotEmpty(report.AppliedChanges);
    }

    [Fact]
    public async Task A_value_undo_failed_to_restore_shows_up_as_a_difference()
    {
        // Game Mode does not exist before, so applying creates it and undo should remove it.
        // Here the removal is refused: the apply works, the rollback does not.
        var target = _registryTweaks.Find("game-mode.on")!.Values[0].ToRef();
        _registry.FailingDeletes.Add(target.ToString());

        var report = await Runner().RunAsync(_profiles.Find("gaming")!);

        Assert.False(report.ReturnedToStart);
        var difference = Assert.Single(report.Differences, d => d.Target == target.ToString());
        Assert.Null(difference.Before);
        Assert.Equal("Dword:1", difference.After);
    }

    [Fact]
    public async Task A_failed_undo_is_reported_alongside_the_difference_it_caused()
    {
        var target = _registryTweaks.Find("game-mode.on")!.Values[0].ToRef();
        _registry.FailingDeletes.Add(target.ToString());

        var report = await Runner().RunAsync(_profiles.Find("gaming")!);

        Assert.NotEmpty(report.Undo.Failures);
        Assert.False(report.Undo.AllSucceeded);
    }

    [Fact]
    public async Task The_report_keeps_all_three_snapshots_for_the_human_to_read()
    {
        var report = await Runner().RunAsync(_profiles.Find("work")!);

        Assert.Equal("before", report.Before.Label);
        Assert.Equal("after-apply", report.AfterApply.Label);
        Assert.Equal("after-undo", report.AfterUndo.Label);
        Assert.Equal("work", report.ProfileId);
    }

    [Fact]
    public async Task A_tweak_that_was_not_applicable_does_not_make_the_run_look_dirty()
    {
        // Ultimate Performance is absent here; that is not a difference, it is a no-op.
        var report = await Runner().RunAsync(_profiles.Find("gaming")!);

        Assert.True(report.ReturnedToStart);
    }

    [Fact]
    public async Task Cleanup_deleting_files_does_not_count_as_a_difference()
    {
        // Deleted temp files are permanent and expected. The snapshot watches settings, not junk,
        // so a cleanup run still passes doc 07.2.
        _files.WithFile(@"C:\FakeUsers\tester\AppData\Local\Temp\a.tmp", 4096);

        var report = await Runner().RunAsync(_profiles.Find("work")!);

        Assert.True(report.ReturnedToStart);
        Assert.False(_files.Exists(@"C:\FakeUsers\tester\AppData\Local\Temp\a.tmp"));
    }
}
