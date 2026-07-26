namespace SystevoTune.Engine.Safety;

/// <summary>
/// Puts changes back, newest first, driven entirely by the change log.
/// One failing step never stops the rest — failures are collected and reported at the end.
/// </summary>
public sealed class UndoEngine
{
    private readonly ChangeLog _log;
    private readonly IReadOnlyDictionary<string, IUndoHandler> _handlers;

    /// <param name="log">The change log to read from and mark up.</param>
    /// <param name="handlers">One handler per module. Two handlers for one module is a build error.</param>
    public UndoEngine(ChangeLog log, IEnumerable<IUndoHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(handlers);

        var byModule = new Dictionary<string, IUndoHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (var handler in handlers)
        {
            if (!byModule.TryAdd(handler.Module, handler))
            {
                throw new InvalidOperationException(
                    $"Two undo handlers are registered for module '{handler.Module}'.");
            }
        }

        _log = log;
        _handlers = byModule;
    }

    /// <summary>
    /// Undoes every record not already undone, across every run, newest change first.
    /// </summary>
    /// <remarks>
    /// Doc 05 section 5.3 describes Undo All as the last run's log. It covers every run instead:
    /// applying twice in a row would otherwise strand the first run's changes with no way back.
    /// Undoing newest-first is safe across runs because each record carries an absolute old value,
    /// so the oldest record always has the last word. Use <see cref="UndoRunAsync"/> for one run.
    /// </remarks>
    public Task<UndoReport> UndoAllAsync(CancellationToken cancellationToken = default)
    {
        var targets = _log.ReadAllRuns().SelectMany(PendingNewestFirst).ToList();
        return UndoAsync(targets, cancellationToken);
    }

    /// <summary>Undoes one run's records, newest first. Leaves other runs alone.</summary>
    /// <exception cref="FileNotFoundException">No log file for that run id.</exception>
    public Task<UndoReport> UndoRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var targets = PendingNewestFirst(_log.ReadRun(runId)).ToList();
        return UndoAsync(targets, cancellationToken);
    }

    /// <summary>Undoes a single record and leaves every other change in place.</summary>
    /// <exception cref="FileNotFoundException">No log file for that run id.</exception>
    public Task<UndoReport> UndoItemAsync(string runId, string recordId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        var run = _log.ReadRun(runId);
        var record = run.Records.FirstOrDefault(r => r.Id == recordId);

        if (record is null)
        {
            return Task.FromResult(new UndoReport([],
                [new UndoFailure(runId, recordId, null, $"Run '{runId}' has no record '{recordId}'.")], []));
        }

        if (record.Undone)
        {
            return Task.FromResult(UndoReport.Empty);
        }

        return UndoAsync([(runId, record)], cancellationToken);
    }

    /// <summary>Records still needing undo, newest change first (reverse of write order).</summary>
    private static IEnumerable<(string RunId, ChangeRecord Record)> PendingNewestFirst(RunLog run)
        => run.Records.Where(record => !record.Undone).Reverse().Select(record => (run.RunId, record));

    private async Task<UndoReport> UndoAsync(
        IReadOnlyList<(string RunId, ChangeRecord Record)> targets,
        CancellationToken cancellationToken)
    {
        var undone = new List<ChangeRecord>();
        var failures = new List<UndoFailure>();
        var permanent = new List<ChangeRecord>();
        var cancelled = false;

        foreach (var (runId, record) in targets)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            // A deleted temp file is gone. Say so rather than reporting a failure for
            // something that was never going to come back.
            if (!record.Undoable)
            {
                permanent.Add(record);
                continue;
            }

            if (!_handlers.TryGetValue(record.Module, out var handler))
            {
                failures.Add(new UndoFailure(runId, record.Id, record,
                    $"No undo handler is registered for module '{record.Module}'."));
                continue;
            }

            try
            {
                await handler.UndoAsync(record, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                // One bad step must not strand the rest. Collect and carry on.
                failures.Add(new UndoFailure(runId, record.Id, record, ex.Message));
                continue;
            }

            // The value is back. Now the log has to agree, or a later pass would undo it twice.
            try
            {
                if (!_log.MarkUndone(runId, record.Id))
                {
                    failures.Add(new UndoFailure(runId, record.Id, record,
                        "Value was restored but the record vanished from the log."));
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new UndoFailure(runId, record.Id, record,
                    $"Value was restored but the log could not be updated: {ex.Message}"));
                continue;
            }

            undone.Add(record with { Undone = true });
        }

        return new UndoReport(undone, failures, permanent, cancelled);
    }
}
