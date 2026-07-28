using SystevoTune.App.Localization;
using SystevoTune.App.ViewModels;
using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Metrics;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Startup;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;
using SystevoTune.Engine.Tweaks.Registry;
using SystevoTune.TestSupport;

namespace SystevoTune.App.Tests;

/// <summary>
/// C3. The states a screen shows when there is nothing to show.
/// </summary>
/// <remarks>
/// Each of these is a moment where a blank panel or a disabled button with no explanation reads as
/// a broken app. They are distinct states on purpose — "this PC has nothing left to change" and
/// "you unticked everything" look identical unless the app says which one it is.
/// </remarks>
public class EdgeStateTests : IDisposable
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid High = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private const string UserTemp = @"C:\FakeUsers\tester\AppData\Local\Temp";

    private readonly TempLogDirectory _directory = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero));
    private readonly FakeRegistryService _registry = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeEnvironmentPaths _environment = new();
    private readonly FakePowerPlanService _powerPlans = new();
    private readonly FakeBatteryStatus _battery = new();
    private readonly ProfileCatalog _profiles = ProfileCatalog.Load();
    private readonly TweakRunner _runner = new();
    private readonly ChangeLog _log;

    public EdgeStateTests()
    {
        _log = new ChangeLog(_directory.Path, _clock);
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
    }

    public void Dispose() => _directory.Dispose();

    private CleanupModule Cleanup() => new(CleanupWhitelist.Load(), _files, _environment);

    private ProfileBuilder Builder() => new(
        Cleanup(), RegistryTweakCatalog.Load(), _registry, _powerPlans, PowerPlanCatalog.Load(), _battery);

    private ProfileApplier Applier() => new(Builder(), _runner);

    private static ILocalizer NewLocalizer() => new Localizer(Localizer.LoadEmbeddedPacks());

    // ---- nothing selected ----

    [Fact]
    public async Task Unticking_everything_is_a_different_state_from_having_nothing_to_offer()
    {
        var model = new ReviewViewModel(_runner, Builder(), _profiles);
        await model.PreviewAsync();

        model.ClearAllCommand.Execute(null);

        Assert.True(model.NothingSelected);
        Assert.False(model.NothingToDo);
        Assert.False(model.CanApply);
    }

    [Fact]
    public async Task A_profile_with_nothing_to_offer_does_not_claim_the_user_unticked_it()
    {
        await Applier().ApplyAsync(_profiles.Find("gaming")!, _log.StartRun());
        var model = new ReviewViewModel(_runner, Builder(), _profiles)
        {
            SelectedProfile = _profiles.Find("gaming"),
        };

        await model.PreviewAsync();

        Assert.True(model.NothingToDo);
        Assert.False(model.NothingSelected);
    }

    [Fact]
    public void Neither_empty_state_shows_before_a_preview_has_run()
    {
        var model = new ReviewViewModel(_runner, Builder(), _profiles);

        Assert.False(model.NothingToDo);
        Assert.False(model.NothingSelected);
    }

    // ---- scan finds nothing ----

    [Fact]
    public async Task A_tidy_pc_is_told_it_is_tidy_rather_than_shown_a_zero()
    {
        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles);

        await model.ScanAsync();

        Assert.True(model.NothingToClean);
        Assert.Equal("0 B", model.TotalFreeable);
    }

    [Fact]
    public async Task A_pc_already_matching_the_profile_says_so()
    {
        await Applier().ApplyAsync(_profiles.Profiles[0], _log.StartRun());
        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles);

        await model.ScanAsync();

        Assert.True(model.NothingToChange);
    }

    // ---- metrics, C4 ----

    [Fact]
    public async Task The_before_panel_stays_hidden_when_there_are_no_metrics()
    {
        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles);

        await model.ScanAsync();

        Assert.False(model.HasMetrics);
        Assert.Equal(string.Empty, model.MemoryUsedDisplay);
    }

    [Fact]
    public async Task Memory_and_startup_counts_are_shown_as_before_values()
    {
        _registry.With(
            new Engine.Platform.RegistryValueRef(
                Engine.Platform.RegistryRoot.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                "OneDrive"),
            Engine.Platform.RegistryValue.Text(@"C:\OneDrive.exe"));

        var metrics = new MetricsCollector(
            new StubMetrics(new MemoryReading(16_000_000_000, 8_000_000_000)),
            new StartupManager(StartupLocationCatalog.Load(), _registry, _files, _environment, _clock),
            Cleanup());

        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles, metrics);
        await model.ScanAsync();

        Assert.True(model.HasMetrics);
        Assert.Contains("7.4 GB", model.MemoryUsedDisplay, StringComparison.Ordinal);
        Assert.Equal("1 / 1", model.StartupAppsDisplay);
    }

    [Fact]
    public async Task A_memory_reading_windows_refuses_is_left_blank_rather_than_invented()
    {
        // Doc 01 rules out numbers we cannot stand behind, and this is the easiest place to
        // accidentally show one.
        var metrics = new MetricsCollector(
            new StubMetrics(null),
            new StartupManager(StartupLocationCatalog.Load(), _registry, _files, _environment, _clock),
            Cleanup());

        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles, metrics);
        await model.ScanAsync();

        Assert.True(model.HasMetrics);
        Assert.Equal(string.Empty, model.MemoryUsedDisplay);
        Assert.Equal("0 / 0", model.StartupAppsDisplay);
    }

    // ---- undo with no runs ----

    [Fact]
    public async Task Undo_with_no_runs_on_disk_reports_nothing_to_do_rather_than_success()
    {
        var model = new ResultsViewModel(
            new UndoEngine(_log, []), new ReapplyService(_log, _profiles), NewLocalizer());

        await model.UndoAllAsync();

        Assert.True(model.UndoFoundNothing);
        Assert.Equal(0, model.UndoneCount);
        Assert.Empty(model.UndoFailures);
        Assert.Null(model.Error);
    }

    [Fact]
    public void Re_apply_is_not_offered_before_anything_has_been_applied()
    {
        var model = new ResultsViewModel(
            new UndoEngine(_log, []), new ReapplyService(_log, _profiles), NewLocalizer());

        model.RefreshReapply();

        Assert.False(model.CanReapply);
        Assert.Null(model.LastProfile);
    }

    [Fact]
    public void A_results_screen_with_no_run_loaded_reports_nothing_applied()
    {
        var model = new ResultsViewModel(
            new UndoEngine(_log, []), new ReapplyService(_log, _profiles), NewLocalizer());

        Assert.True(model.NothingApplied);
        Assert.Equal(0, model.AppliedCount);
        Assert.Equal("0 B", model.FreedSpace);
    }

    // ---- the empty log ----

    [Fact]
    public async Task An_empty_log_folder_is_distinguishable_from_one_not_yet_read()
    {
        var model = new LogViewerViewModel(_log, NewLocalizer());

        Assert.False(model.IsEmpty);

        await model.RefreshAsync();

        Assert.True(model.IsEmpty);
        Assert.Equal(0, model.TotalChanges);
    }

    // ---- language switching does not disturb state ----

    [Fact]
    public async Task Switching_language_mid_review_keeps_the_selection()
    {
        var localizer = new Localizer(Localizer.LoadEmbeddedPacks());
        var model = new ReviewViewModel(_runner, Builder(), _profiles);
        await model.PreviewAsync();
        model.AllRows[0].IsSelected = false;
        var selected = model.SelectedCount;

        localizer.Use(Language.Arabic);

        Assert.Equal(selected, model.SelectedCount);
        Assert.True(model.IsCustom);
    }

    /// <summary>A memory reading a test controls.</summary>
    private sealed class StubMetrics(MemoryReading? reading) : ISystemMetrics
    {
        public MemoryReading? ReadMemory() => reading;
    }
}
