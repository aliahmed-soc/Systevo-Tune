using SystevoTune.Engine.Safety;

namespace SystevoTune.Engine.Tests.Safety;

public class UndoEngineTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 7, 26, 14, 3, 22, TimeSpan.Zero);

    private readonly TempLogDirectory _directory = new();
    private readonly FixedClock _clock = new(Noon);
    private readonly FakeSystem _system = new();
    private readonly ChangeLog _log;
    private readonly FakeUndoHandler _handler;

    public UndoEngineTests()
    {
        _log = new ChangeLog(_directory.Path, _clock);
        _handler = new FakeUndoHandler(_system);
    }

    public void Dispose() => _directory.Dispose();

    private UndoEngine NewEngine() => new(_log, [_handler]);

    /// <summary>Log first, change second — the order every module must follow.</summary>
    private void ApplyChange(ChangeLogRun run, string target, string? newValue)
    {
        run.RecordChange("Fake", "SetValue", target, _system.Get(target), newValue);
        _system.Set(target, newValue);
    }

    // ---- ordering ----

    [Fact]
    public async Task Undo_all_walks_records_newest_first()
    {
        var run = _log.StartRun();
        ApplyChange(run, "a", "1");
        ApplyChange(run, "b", "2");
        ApplyChange(run, "c", "3");

        await NewEngine().UndoAllAsync();

        Assert.Equal(["2026-07-26-003", "2026-07-26-002", "2026-07-26-001"], _handler.UndoneIds);
    }

    [Fact]
    public async Task Undo_all_restores_the_original_value_of_a_target_changed_twice()
    {
        var run = _log.StartRun();
        ApplyChange(run, "shared", "first");
        ApplyChange(run, "shared", "second");

        await NewEngine().UndoAllAsync();

        Assert.Null(_system.Get("shared"));
    }

    [Fact]
    public async Task Undo_all_crosses_runs_newest_run_first()
    {
        _system.Set("power", "balanced");
        var first = _log.StartRun();
        ApplyChange(first, "power", "high");

        _clock.Advance(TimeSpan.FromMinutes(10));
        var second = _log.StartRun();
        ApplyChange(second, "power", "ultimate");

        var report = await NewEngine().UndoAllAsync();

        Assert.True(report.AllSucceeded);
        Assert.Equal(2, report.Undone.Count);
        Assert.Equal("balanced", _system.Get("power"));
    }

    // ---- double run ----

    [Fact]
    public async Task Applying_the_same_change_twice_still_undoes_to_the_original_value()
    {
        _system.Set("power", "balanced");
        var first = _log.StartRun();
        ApplyChange(first, "power", "high");

        _clock.Advance(TimeSpan.FromMinutes(10));
        var second = _log.StartRun();
        ApplyChange(second, "power", "high");

        await NewEngine().UndoAllAsync();

        Assert.Equal("balanced", _system.Get("power"));
    }

    [Fact]
    public async Task A_second_undo_all_does_nothing_because_records_are_marked_undone()
    {
        var run = _log.StartRun();
        ApplyChange(run, "a", "1");
        ApplyChange(run, "b", "2");
        var engine = NewEngine();
        await engine.UndoAllAsync();

        var second = await engine.UndoAllAsync();

        Assert.Equal(0, second.AttemptedCount);
        Assert.Equal(2, _handler.UndoneIds.Count);
    }

    [Fact]
    public async Task Undone_records_are_marked_on_disk()
    {
        var run = _log.StartRun();
        ApplyChange(run, "a", "1");

        await NewEngine().UndoAllAsync();

        Assert.True(Assert.Single(_log.ReadRun(run.RunId).Records).Undone);
    }

    // ---- partial failure ----

    [Fact]
    public async Task Undo_continues_after_one_step_fails()
    {
        var run = _log.StartRun();
        ApplyChange(run, "a", "1");
        ApplyChange(run, "locked", "2");
        ApplyChange(run, "c", "3");
        _handler.FailingTargets.Add("locked");

        var report = await NewEngine().UndoAllAsync();

        Assert.Equal(2, report.Undone.Count);
        Assert.Null(_system.Get("a"));
        Assert.Null(_system.Get("c"));
    }

    [Fact]
    public async Task A_failed_step_is_reported_with_its_target_and_reason()
    {
        var run = _log.StartRun();
        ApplyChange(run, "locked", "2");
        _handler.FailingTargets.Add("locked");

        var report = await NewEngine().UndoAllAsync();

        var failure = Assert.Single(report.Failures);
        Assert.False(report.AllSucceeded);
        Assert.Equal("locked", failure.Record?.Target);
        Assert.Contains("locked by another process", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_step_is_not_marked_undone_so_it_can_be_retried()
    {
        var run = _log.StartRun();
        ApplyChange(run, "locked", "2");
        _handler.FailingTargets.Add("locked");
        await NewEngine().UndoAllAsync();

        Assert.False(Assert.Single(_log.ReadRun(run.RunId).Records).Undone);

        _handler.FailingTargets.Clear();
        var retry = await NewEngine().UndoAllAsync();

        Assert.True(retry.AllSucceeded);
        Assert.Null(_system.Get("locked"));
    }

    [Fact]
    public async Task A_record_with_no_handler_is_reported_instead_of_crashing_the_pass()
    {
        var run = _log.StartRun();
        run.RecordChange("ModuleFromAnOlderBuild", "SetValue", "orphan", "old", "new");
        ApplyChange(run, "a", "1");

        var report = await NewEngine().UndoAllAsync();

        Assert.Single(report.Undone);
        Assert.Contains("No undo handler", Assert.Single(report.Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_stops_the_pass_and_says_so()
    {
        var run = _log.StartRun();
        ApplyChange(run, "a", "1");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var report = await NewEngine().UndoAllAsync(cancellation.Token);

        Assert.True(report.Cancelled);
        Assert.False(report.AllSucceeded);
        Assert.Equal(0, report.AttemptedCount);
    }

    // ---- scoped undo ----

    [Fact]
    public async Task Undo_run_leaves_other_runs_alone()
    {
        _system.Set("power", "balanced");
        var first = _log.StartRun();
        ApplyChange(first, "power", "high");

        _clock.Advance(TimeSpan.FromMinutes(10));
        var second = _log.StartRun();
        ApplyChange(second, "visuals", "off");

        await NewEngine().UndoRunAsync(second.RunId);

        Assert.Null(_system.Get("visuals"));
        Assert.Equal("high", _system.Get("power"));
        Assert.False(Assert.Single(_log.ReadRun(first.RunId).Records).Undone);
    }

    [Fact]
    public async Task Undo_item_reverts_only_that_record()
    {
        var run = _log.StartRun();
        ApplyChange(run, "a", "1");
        var second = run.RecordChange("Fake", "SetValue", "b", _system.Get("b"), "2");
        _system.Set("b", "2");

        await NewEngine().UndoItemAsync(run.RunId, second.Id);

        Assert.Null(_system.Get("b"));
        Assert.Equal("1", _system.Get("a"));
    }

    [Fact]
    public async Task Undo_item_reports_a_failure_for_an_unknown_record()
    {
        var run = _log.StartRun();
        ApplyChange(run, "a", "1");

        var report = await NewEngine().UndoItemAsync(run.RunId, "2026-07-26-999");

        Assert.Contains("no record", Assert.Single(report.Failures).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1", _system.Get("a"));
    }

    [Fact]
    public async Task Undo_item_on_an_already_undone_record_does_nothing()
    {
        var run = _log.StartRun();
        ApplyChange(run, "a", "1");
        var engine = NewEngine();
        await engine.UndoItemAsync(run.RunId, "2026-07-26-001");

        var again = await engine.UndoItemAsync(run.RunId, "2026-07-26-001");

        Assert.Equal(0, again.AttemptedCount);
        Assert.Single(_handler.UndoneIds);
    }

    // ---- wiring ----

    [Fact]
    public void Two_handlers_for_one_module_is_a_build_error()
        => Assert.Throws<InvalidOperationException>(
            () => new UndoEngine(_log, [new FakeUndoHandler(_system), new FakeUndoHandler(_system)]));

    [Fact]
    public async Task Undo_all_on_an_empty_log_reports_nothing_attempted()
    {
        var report = await NewEngine().UndoAllAsync();

        Assert.True(report.AllSucceeded);
        Assert.Equal(0, report.AttemptedCount);
    }
}
