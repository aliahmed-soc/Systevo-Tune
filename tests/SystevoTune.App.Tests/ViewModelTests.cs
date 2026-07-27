using System.IO;
using SystevoTune.App.ViewModels;
using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;
using SystevoTune.Engine.Tweaks.Registry;
using SystevoTune.TestSupport;

namespace SystevoTune.App.Tests;

/// <summary>
/// A9. Every screen's view model, covering the happy path, warnings, an engine that throws, an
/// empty scan, a partial undo, and needs-restart aggregation.
/// </summary>
/// <remarks>
/// None of these touch a UI thread or a real Windows API — the view models take engine types
/// built over Fakes, which is the whole reason they are shaped that way.
/// </remarks>
public class ViewModelTests : IDisposable
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid High = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private const string UserTemp = @"C:\FakeUsers\tester\AppData\Local\Temp";

    private readonly TempLogDirectory _directory = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeRegistryService _registry = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeEnvironmentPaths _environment = new();
    private readonly FakePowerPlanService _powerPlans = new();
    private readonly FakeBatteryStatus _battery = new();
    private readonly ProfileCatalog _profiles = ProfileCatalog.Load();
    private readonly RegistryTweakCatalog _registryTweaks = RegistryTweakCatalog.Load();
    private readonly TweakRunner _runner = new();
    private readonly ChangeLog _log;

    public ViewModelTests()
    {
        _log = new ChangeLog(_directory.Path, _clock);
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
    }

    public void Dispose() => _directory.Dispose();

    private CleanupModule Cleanup() => new(CleanupWhitelist.Load(), _files, _environment);

    private ProfileBuilder Builder() => new(
        Cleanup(), _registryTweaks, _registry, _powerPlans, PowerPlanCatalog.Load(), _battery);

    private ProfileApplier Applier() => new(Builder(), _runner);

    private UndoEngine Undo(params IUndoHandler[] handlers) => new(
        _log, handlers.Length > 0 ? handlers : [new RegistryUndoHandler(_registry), new PowerPlanUndoHandler(_powerPlans)]);

    private ReapplyService Reapply() => new(_log, _profiles);

    // ================= Scan =================

    [Fact]
    public async Task Scan_happy_path_reports_sizes_and_states()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 4096);
        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles);

        await model.ScanAsync();

        Assert.Null(model.Error);
        Assert.NotEmpty(model.CleanupGroups);
        Assert.NotEmpty(model.Tweaks);
        Assert.Equal(4096, model.TotalFreeableBytes);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Scan_changes_nothing()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 4096);
        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles);

        await model.ScanAsync();

        Assert.Empty(_files.Deleted);
        Assert.Empty(_registry.Writes);
        Assert.Empty(_powerPlans.Activated);
        Assert.Empty(_log.ListRunIds());
    }

    [Fact]
    public async Task Scan_with_nothing_to_clean_says_so_rather_than_showing_zero()
    {
        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles);

        await model.ScanAsync();

        Assert.True(model.NothingToClean);
        Assert.Equal(0, model.TotalFreeableBytes);
    }

    [Fact]
    public async Task Scan_distinguishes_not_run_yet_from_found_nothing()
    {
        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles);

        Assert.False(model.NothingToClean);
        Assert.False(model.HasScanned);

        await model.ScanAsync();

        Assert.True(model.HasScanned);
    }

    [Fact]
    public async Task Scan_reports_an_engine_failure_instead_of_throwing()
    {
        // The folder has to exist for the scan to try walking it.
        _files.WithDirectory(UserTemp);
        _files.EnumerateFailure = new IOException("the disk went away");
        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles);

        await model.ScanAsync();

        Assert.Equal("the disk went away", model.Error);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Scan_notices_when_every_setting_is_already_right()
    {
        // Applying the profile is how a PC actually reaches that state — seeding the raw values
        // by hand would set the same key twice, because Gaming and Work want opposite values.
        await Applier().ApplyAsync(_profiles.Profiles[0], _log.StartRun());

        var model = new ScanViewModel(Cleanup(), _runner, Builder(), _profiles);
        await model.ScanAsync();

        Assert.True(model.NothingToChange);
    }

    // ================= Review =================

    [Fact]
    public async Task Review_lists_changes_grouped_by_tweak_and_selects_them_all()
    {
        var model = new ReviewViewModel(_runner, Builder(), _profiles);

        await model.PreviewAsync();

        Assert.NotEmpty(model.Groups);
        Assert.Equal(model.TotalCount, model.SelectedCount);
        Assert.True(model.CanApply);
    }

    [Fact]
    public async Task Review_applies_nothing()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1024);
        var model = new ReviewViewModel(_runner, Builder(), _profiles);

        await model.PreviewAsync();

        Assert.Empty(_registry.Writes);
        Assert.Empty(_files.Deleted);
        Assert.Empty(_log.ListRunIds());
    }

    [Fact]
    public async Task Unticking_a_change_makes_the_run_custom()
    {
        var model = new ReviewViewModel(_runner, Builder(), _profiles);
        await model.PreviewAsync();

        Assert.False(model.IsCustom);

        model.AllRows[0].IsSelected = false;

        Assert.True(model.IsCustom);
        Assert.Equal(model.TotalCount - 1, model.SelectedCount);
    }

    [Fact]
    public async Task Clearing_everything_disables_apply()
    {
        var model = new ReviewViewModel(_runner, Builder(), _profiles);
        await model.PreviewAsync();

        model.ClearAllCommand.Execute(null);

        Assert.Equal(0, model.SelectedCount);
        Assert.False(model.CanApply);
    }

    [Fact]
    public async Task Select_all_puts_everything_back()
    {
        var model = new ReviewViewModel(_runner, Builder(), _profiles);
        await model.PreviewAsync();
        model.ClearAllCommand.Execute(null);

        model.SelectAllCommand.Execute(null);

        Assert.Equal(model.TotalCount, model.SelectedCount);
    }

    [Fact]
    public async Task A_whole_group_can_be_ticked_at_once()
    {
        var model = new ReviewViewModel(_runner, Builder(), _profiles);
        await model.PreviewAsync();
        var group = model.Groups[0];

        model.SetGroup(group.TweakId, selected: false);

        Assert.All(group.Rows, row => Assert.False(row.IsSelected));
    }

    [Fact]
    public async Task Review_flags_a_permanent_change_so_the_user_sees_it_before_ticking()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1024);
        var model = new ReviewViewModel(_runner, Builder(), _profiles);

        await model.PreviewAsync();

        Assert.Contains(model.AllRows, row => row.IsPermanent);
    }

    [Fact]
    public async Task A_tweak_that_throws_while_looking_drops_out_rather_than_taking_the_screen_down()
    {
        // The runner turns a throwing tweak into a Blocked plan, so Review keeps working and
        // simply has nothing to offer for that tweak.
        _files.WithDirectory(UserTemp);
        _files.EnumerateFailure = new IOException("no disk");
        var model = new ReviewViewModel(_runner, Builder(), _profiles);

        await model.PreviewAsync();

        Assert.Null(model.Error);
        Assert.DoesNotContain(model.Groups, group => group.TweakId.StartsWith("cleanup.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Review_reports_a_broken_profile_instead_of_throwing()
    {
        // A profile naming a tweak that no longer exists — the builder throws, and the screen has
        // to say so rather than disappear.
        var broken = ProfileCatalog.Parse("""
            {"id":"broken","nameEn":"Broken","nameAr":"معطل",
             "steps":[{"kind":"registry","id":"a-tweak-that-was-removed"}]}
            """);

        var model = new ReviewViewModel(_runner, Builder(), _profiles) { SelectedProfile = broken };

        await model.PreviewAsync();

        Assert.NotNull(model.Error);
        Assert.Contains("not in the registry whitelist", model.Error, StringComparison.Ordinal);
        Assert.False(model.CanApply);
    }

    [Fact]
    public async Task A_profile_with_nothing_left_to_do_says_so()
    {
        var gaming = _profiles.Find("gaming")!;
        await Applier().ApplyAsync(gaming, _log.StartRun());

        var model = new ReviewViewModel(_runner, Builder(), _profiles) { SelectedProfile = gaming };
        await model.PreviewAsync();

        Assert.True(model.NothingToDo);
        Assert.False(model.CanApply);
    }

    // ================= Confirm =================

    [Fact]
    public async Task Confirm_shows_no_warning_when_a_restore_point_was_created()
    {
        var model = new ConfirmApplyViewModel(
            new StubRestorePoints(new RestorePointResult(RestorePointStatus.Created, "made one")), 3, "before Gaming");

        await model.PrepareAsync();

        Assert.False(model.HasWarning);
        Assert.True(model.RestorePointCreated);
    }

    [Theory]
    [InlineData(RestorePointStatus.Disabled, "Confirm_RestoreDisabled")]
    [InlineData(RestorePointStatus.Skipped, "Confirm_RestoreSkipped")]
    [InlineData(RestorePointStatus.Failed, "Confirm_RestoreFailed")]
    public async Task Confirm_warns_in_red_for_every_outcome_that_is_not_created(
        RestorePointStatus status,
        string expectedKey)
    {
        // A6: the user must read past this before they can continue.
        var model = new ConfirmApplyViewModel(
            new StubRestorePoints(new RestorePointResult(status, "engine wording")), 3, "before Gaming");

        await model.PrepareAsync();

        Assert.True(model.HasWarning);
        Assert.Equal(expectedKey, model.WarningKey);
        Assert.Equal("engine wording", model.EngineMessage);
    }

    [Fact]
    public async Task Confirm_warns_when_the_user_has_switched_restore_points_off()
    {
        // B4: turning the safety net off is exactly the moment to say so again.
        var model = new ConfirmApplyViewModel(
            new StubRestorePoints(new RestorePointResult(RestorePointStatus.Created, "unused")),
            3,
            "before Gaming",
            restorePointsWanted: false);

        await model.PrepareAsync();

        Assert.True(model.HasWarning);
        Assert.Equal("Confirm_RestoreOff", model.WarningKey);
    }

    [Fact]
    public async Task Confirm_attempts_nothing_when_restore_points_are_switched_off()
    {
        var restore = new StubRestorePoints(new RestorePointResult(RestorePointStatus.Created, "x"));
        var model = new ConfirmApplyViewModel(restore, 1, "before Gaming", restorePointsWanted: false);

        await model.PrepareAsync();

        Assert.Equal(0, restore.Calls);
    }

    [Fact]
    public async Task A_restore_service_that_throws_becomes_a_warning_not_a_crash()
    {
        var model = new ConfirmApplyViewModel(new ThrowingRestorePoints(), 1, "before Gaming");

        await model.PrepareAsync();

        Assert.True(model.HasWarning);
        Assert.Equal("Confirm_RestoreFailed", model.WarningKey);
    }

    [Fact]
    public void Confirm_starts_unconfirmed()
        => Assert.False(new ConfirmApplyViewModel(
            new StubRestorePoints(new RestorePointResult(RestorePointStatus.Created, "x")), 1, "d").Confirmed);

    // ================= Apply =================

    [Fact]
    public async Task Apply_streams_one_row_per_tweak_as_it_goes()
    {
        var model = new ApplyViewModel(Applier(), _log, uiContext: null);

        await model.RunAsync(_profiles.Find("work")!);

        Assert.NotEmpty(model.Outcomes);
        Assert.True(model.IsFinished);
        Assert.False(model.IsRunning);
        Assert.NotNull(model.RunId);
    }

    [Fact]
    public async Task Apply_marks_a_failed_tweak_as_failed_with_the_reason()
    {
        var target = _registryTweaks.Find("game-mode.off")!.Values[0].ToRef();
        _registry.FailingTargets.Add(target.ToString());
        var model = new ApplyViewModel(Applier(), _log, uiContext: null);

        await model.RunAsync(_profiles.Find("work")!);

        var failed = Assert.Single(model.Outcomes, row => row.Kind == ApplyOutcomeKind.Failed);
        Assert.NotNull(failed.Detail);
        Assert.Contains("not writable", failed.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_marks_a_tweak_with_nothing_to_do_as_such_rather_than_as_a_failure()
    {
        await _powerPlans.SetActiveAsync(Balanced, CancellationToken.None);
        var model = new ApplyViewModel(Applier(), _log, uiContext: null);

        await model.RunAsync(_profiles.Find("work")!);

        Assert.Contains(model.Outcomes, row => row.Kind == ApplyOutcomeKind.NothingToDo);
    }

    [Fact]
    public void Apply_gathers_needs_restart_flags_and_reports_them_once()
    {
        // A4: collected as the run goes, shown at the end — not nagged per tweak.
        var model = new ApplyViewModel(Applier(), _log, uiContext: null);
        var record = new ChangeRecord
        {
            Id = "2026-07-27-001",
            Time = new DateTime(2026, 7, 27, 12, 0, 0),
            Module = "Registry",
            Action = "SetValue",
            Target = "x",
        };

        model.Record(new TweakOutcome("a", "GPU scheduling", TweakStatus.Ready, [record], [], null, RequiresRestart: true));
        model.Record(new TweakOutcome("b", "Services", TweakStatus.Ready, [record], [], null, RequiresRestart: true));
        model.Record(new TweakOutcome("c", "Game Mode", TweakStatus.Ready, [record], [], null, RequiresRestart: false));

        Assert.True(model.RequiresRestart);
        Assert.Equal(["GPU scheduling", "Services"], model.NeedsRestart);
    }

    [Fact]
    public void A_tweak_needing_a_restart_that_applied_nothing_does_not_ask_for_one()
    {
        var model = new ApplyViewModel(Applier(), _log, uiContext: null);

        model.Record(new TweakOutcome("a", "GPU", TweakStatus.AlreadyApplied, [], [], null, RequiresRestart: true));

        Assert.False(model.RequiresRestart);
    }

    [Fact]
    public async Task Apply_writes_a_run_that_undo_can_find()
    {
        var model = new ApplyViewModel(Applier(), _log, uiContext: null);

        await model.RunAsync(_profiles.Find("work")!);

        Assert.Single(_log.ListRunIds());
        Assert.Equal("work", _log.ReadAllRuns()[0].ProfileId);
    }

    // ================= Results =================

    [Fact]
    public async Task Results_summarises_a_finished_run()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 2048);
        var apply = new ApplyViewModel(Applier(), _log, uiContext: null);
        await apply.RunAsync(_profiles.Find("work")!);
        var model = new ResultsViewModel(Undo(), Reapply());

        model.Load(apply.Result!);

        Assert.True(model.AppliedCount > 0);
        Assert.Equal(2048, model.FreedBytes);
        Assert.False(model.NothingApplied);
    }

    [Fact]
    public async Task Undo_all_puts_everything_back_and_reports_the_count()
    {
        var apply = new ApplyViewModel(Applier(), _log, uiContext: null);
        await apply.RunAsync(_profiles.Find("work")!);
        var model = new ResultsViewModel(Undo(), Reapply());
        model.Load(apply.Result!);

        await model.UndoAllAsync();

        Assert.True(model.HasUndone);
        Assert.True(model.UndoneCount > 0);
        Assert.Empty(model.UndoFailures);
        Assert.Equal(Balanced, _powerPlans.Active);
    }

    [Fact]
    public async Task A_partial_undo_failure_is_surfaced_rather_than_swallowed()
    {
        // Doc 5.3: keep going, then show a clear list of what failed.
        var apply = new ApplyViewModel(Applier(), _log, uiContext: null);
        await apply.RunAsync(_profiles.Find("work")!);

        var stuck = _registryTweaks.Find("game-mode.off")!.Values[0].ToRef();
        _registry.FailingDeletes.Add(stuck.ToString());
        _registry.FailingTargets.Add(stuck.ToString());

        var model = new ResultsViewModel(Undo(), Reapply());
        model.Load(apply.Result!);

        await model.UndoAllAsync();

        Assert.NotEmpty(model.UndoFailures);
        Assert.True(model.UndoWasPartial);
        Assert.Contains(model.UndoFailures, row => row.Target.Contains("AutoGameModeEnabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Permanent_changes_are_listed_apart_from_failures()
    {
        // A deleted temp file is not a failure, and must not read as one.
        _files.WithFile($@"{UserTemp}\a.tmp", 1024);
        var apply = new ApplyViewModel(Applier(), _log, uiContext: null);
        await apply.RunAsync(_profiles.Find("work")!);
        var model = new ResultsViewModel(Undo(), Reapply());
        model.Load(apply.Result!);

        await model.UndoAllAsync();

        Assert.Contains("temp-files", model.PermanentChanges);
        Assert.Empty(model.UndoFailures);
    }

    [Fact]
    public async Task Undo_with_no_runs_on_disk_says_there_was_nothing_to_do()
    {
        var model = new ResultsViewModel(Undo(), Reapply());

        await model.UndoAllAsync();

        Assert.True(model.UndoFoundNothing);
        Assert.Empty(model.UndoFailures);
    }

    [Fact]
    public async Task Re_apply_is_offered_once_a_profile_has_been_applied()
    {
        var model = new ResultsViewModel(Undo(), Reapply());
        model.RefreshReapply();
        Assert.False(model.CanReapply);

        var apply = new ApplyViewModel(Applier(), _log, uiContext: null);
        await apply.RunAsync(_profiles.Find("gaming")!);
        model.RefreshReapply();

        Assert.True(model.CanReapply);
        Assert.Equal("gaming", model.LastProfile!.ProfileId);
    }

    [Fact]
    public async Task Results_reports_needs_restart_from_the_run()
    {
        _registry.With(_registryTweaks.Find("gpu-scheduling.on")!.Values[0].ToRef(), RegistryValue.Dword(1));
        var apply = new ApplyViewModel(Applier(), _log, uiContext: null);
        await apply.RunAsync(_profiles.Find("gaming")!);
        var model = new ResultsViewModel(Undo(), Reapply());

        model.Load(apply.Result!);

        Assert.True(model.RequiresRestart);
    }

    /// <summary>A restore point service that returns a fixed answer.</summary>
    private sealed class StubRestorePoints(RestorePointResult result) : IRestorePointService
    {
        public int Calls { get; private set; }

        public bool IsSystemRestoreEnabled() => true;

        public Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    /// <summary>A restore point service that misbehaves.</summary>
    private sealed class ThrowingRestorePoints : IRestorePointService
    {
        public bool IsSystemRestoreEnabled() => true;

        public Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken)
            => throw new InvalidOperationException("something unexpected");
    }
}
