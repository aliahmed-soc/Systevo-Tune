using SystevoTune.Engine.Safety;
using SystevoTune.TestSupport;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.Engine.Tests.Tweaks;

public class TweakRunnerTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 7, 26, 14, 3, 22, TimeSpan.Zero);

    private readonly TempLogDirectory _directory = new();
    private readonly ChangeLog _log;
    private readonly TweakRunner _runner = new();

    public TweakRunnerTests() => _log = new ChangeLog(_directory.Path, new FixedClock(Noon));

    public void Dispose() => _directory.Dispose();

    // ---- preview changes nothing ----

    [Fact]
    public async Task Preview_never_applies_a_change()
    {
        var tweak = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");

        await _runner.PreviewAsync([tweak]);

        Assert.Empty(tweak.Applied);
    }

    [Fact]
    public async Task Preview_never_opens_a_log_run()
    {
        var tweak = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");

        await _runner.PreviewAsync([tweak]);

        Assert.Empty(_log.ListRunIds());
    }

    [Fact]
    public async Task Preview_reports_the_old_and_new_value_of_every_change()
    {
        var power = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");
        var visuals = new FakeTweak("visuals").Changing("VisualFXSetting", "1", "2");

        var preview = await _runner.PreviewAsync([power, visuals]);

        Assert.Equal(2, preview.AllChanges.Count);
        Assert.Equal(["balanced", "1"], preview.AllChanges.Select(change => change.OldValue));
        Assert.Equal(["high", "2"], preview.AllChanges.Select(change => change.NewValue));
    }

    [Fact]
    public async Task Preview_separates_tweaks_with_nothing_to_do()
    {
        var ready = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");
        var done = new FakeTweak("visuals") { Plan = TweakPlan.AlreadyApplied("visuals", "visuals", "already off") };

        var preview = await _runner.PreviewAsync([ready, done]);

        Assert.Equal(2, preview.Plans.Count);
        Assert.Equal("power", Assert.Single(preview.Actionable).TweakId);
    }

    [Fact]
    public async Task A_tweak_that_throws_while_looking_is_blocked_not_fatal()
    {
        var broken = new FakeTweak("broken") { PlanFailure = new UnauthorizedAccessException("needs admin") };
        var fine = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");

        var preview = await _runner.PreviewAsync([broken, fine]);

        Assert.Equal(TweakStatus.Blocked, preview.Plans[0].Status);
        Assert.Contains("needs admin", preview.Plans[0].Message!, StringComparison.Ordinal);
        Assert.Single(preview.Actionable);
    }

    [Fact]
    public async Task Preview_reports_when_a_restart_would_be_needed()
    {
        var gpu = new FakeTweak("gpu")
        {
            Plan = TweakPlan.Ready("gpu", "GPU scheduling",
                [new PlannedChange("Registry", "SetValue", "HwSchMode", "1", "2", "on")], requiresRestart: true),
        };

        var preview = await _runner.PreviewAsync([gpu]);

        Assert.True(preview.RequiresRestart);
    }

    [Fact]
    public async Task A_restart_is_not_claimed_for_a_tweak_with_nothing_to_do()
    {
        var gpu = new FakeTweak("gpu")
        {
            Plan = new TweakPlan("gpu", "GPU scheduling", TweakStatus.AlreadyApplied, [], "already on", RequiresRestart: true),
        };

        var preview = await _runner.PreviewAsync([gpu]);

        Assert.False(preview.RequiresRestart);
    }

    // ---- log first, change second ----

    [Fact]
    public async Task The_log_record_is_already_on_disk_when_the_change_runs()
    {
        var run = _log.StartRun();
        var seenOnDisk = new List<string>();
        var tweak = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");
        tweak.OnApply = _ => seenOnDisk.AddRange(File.ReadAllLines(run.FilePath));

        await _runner.ApplyAsync([tweak], run);

        Assert.Contains("ActivePowerScheme", Assert.Single(seenOnDisk), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_change_that_cannot_be_logged_is_not_applied()
    {
        var run = _log.StartRun();
        var tweak = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");
        File.Delete(run.FilePath);
        Directory.CreateDirectory(run.FilePath); // a directory where the log file should be

        var report = await _runner.ApplyAsync([tweak], run);

        Assert.Empty(tweak.Applied);
        Assert.Contains("change log", Assert.Single(report.AllFailures).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_replans_so_old_values_come_from_the_live_system()
    {
        var run = _log.StartRun();
        var tweak = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");

        await _runner.PreviewAsync([tweak]);
        await _runner.ApplyAsync([tweak], run);

        Assert.Equal(2, tweak.PlanCount);
    }

    // ---- apply results ----

    [Fact]
    public async Task Applying_writes_one_record_per_change()
    {
        var run = _log.StartRun();
        var tweak = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");

        var report = await _runner.ApplyAsync([tweak], run);

        Assert.True(report.AllSucceeded);
        var record = Assert.Single(_log.ReadRun(run.RunId).Records);
        Assert.Equal("ActivePowerScheme", record.Target);
        Assert.Equal("balanced", record.OldValue);
        Assert.Equal("high", record.NewValue);
    }

    [Fact]
    public async Task A_tweak_with_nothing_to_do_writes_no_records()
    {
        var run = _log.StartRun();
        var done = new FakeTweak("visuals") { Plan = TweakPlan.AlreadyApplied("visuals", "visuals", "already off") };

        var report = await _runner.ApplyAsync([done], run);

        Assert.Empty(_log.ReadRun(run.RunId).Records);
        Assert.Equal(TweakStatus.AlreadyApplied, Assert.Single(report.Outcomes).Status);
    }

    [Fact]
    public async Task One_failing_change_does_not_stop_the_others()
    {
        var run = _log.StartRun();
        var first = new FakeTweak("first").Changing("a", "1", "2");
        var broken = new FakeTweak("broken").Changing("locked", "1", "2");
        broken.FailingTargets.Add("locked");
        var last = new FakeTweak("last").Changing("c", "1", "2");

        var report = await _runner.ApplyAsync([first, broken, last], run);

        Assert.Equal(2, report.AllApplied.Count);
        Assert.Contains("refused the write", Assert.Single(report.AllFailures).Reason, StringComparison.Ordinal);
        Assert.False(report.AllSucceeded);
    }

    [Fact]
    public async Task A_failed_change_still_leaves_its_record_behind()
    {
        var run = _log.StartRun();
        var broken = new FakeTweak("broken").Changing("locked", "1", "2");
        broken.FailingTargets.Add("locked");

        await _runner.ApplyAsync([broken], run);

        Assert.Equal("locked", Assert.Single(_log.ReadRun(run.RunId).Records).Target);
    }

    [Fact]
    public async Task A_permanent_change_is_marked_un_undoable_in_the_log()
    {
        var run = _log.StartRun();
        var cleanup = new FakeTweak("cleanup").Changing(@"C:\Windows\Temp\a.tmp", "4096", null, undoable: false);

        await _runner.ApplyAsync([cleanup], run);

        Assert.False(Assert.Single(_log.ReadRun(run.RunId).Records).Undoable);
    }

    [Fact]
    public async Task Apply_reports_a_needed_restart_only_when_something_was_applied()
    {
        var run = _log.StartRun();
        var gpu = new FakeTweak("gpu")
        {
            Plan = TweakPlan.Ready("gpu", "GPU scheduling",
                [new PlannedChange("Fake", "SetValue", "HwSchMode", "1", "2", "on")], requiresRestart: true),
        };

        var report = await _runner.ApplyAsync([gpu], run);

        Assert.True(report.RequiresRestart);
    }

    [Fact]
    public async Task Cancelling_apply_stops_the_run_and_says_so()
    {
        var run = _log.StartRun();
        var tweak = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var report = await _runner.ApplyAsync([tweak], run, progress: null, cancellation.Token);

        Assert.True(report.Cancelled);
        Assert.Empty(tweak.Applied);
    }

    // ---- apply then undo, through the real pipeline ----

    [Fact]
    public async Task Everything_applied_can_be_undone_again()
    {
        var run = _log.StartRun();
        var system = new FakeSystem();
        system.Set("ActivePowerScheme", "balanced");
        var tweak = new FakeTweak("power").Changing("ActivePowerScheme", "balanced", "high");
        tweak.OnApply = change => system.Set(change.Target, change.NewValue);
        await _runner.ApplyAsync([tweak], run);

        var undo = await new UndoEngine(_log, [new FakeUndoHandler(system)]).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal("balanced", system.Get("ActivePowerScheme"));
    }

    [Fact]
    public async Task Undo_reports_permanent_changes_as_un_restorable_rather_than_failed()
    {
        var run = _log.StartRun();
        var system = new FakeSystem();
        var cleanup = new FakeTweak("cleanup").Changing(@"C:\Windows\Temp\a.tmp", "4096", null, undoable: false);
        await _runner.ApplyAsync([cleanup], run);

        var undo = await new UndoEngine(_log, [new FakeUndoHandler(system)]).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(0, undo.AttemptedCount);
        Assert.Equal(@"C:\Windows\Temp\a.tmp", Assert.Single(undo.Permanent).Target);
    }
}
