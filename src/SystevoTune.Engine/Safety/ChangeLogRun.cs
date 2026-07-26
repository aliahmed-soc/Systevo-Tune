using System.Globalization;
using System.Text.Json;

namespace SystevoTune.Engine.Safety;

/// <summary>
/// A run that is open for writing. Every change the engine makes during the run gets one
/// record here, written to disk before the change is applied.
/// </summary>
public sealed class ChangeLogRun
{
    private readonly string _idPrefix;
    private readonly TimeProvider _time;
    private readonly List<ChangeRecord> _records = [];
    private int _nextSequence;

    internal ChangeLogRun(string runId, string filePath, DateTime startedAt, int firstSequence, TimeProvider time)
    {
        RunId = runId;
        FilePath = filePath;
        StartedAt = startedAt;
        _idPrefix = startedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-";
        _nextSequence = firstSequence;
        _time = time;
    }

    /// <summary>Run identifier, also the log file name without prefix or extension.</summary>
    public string RunId { get; }

    /// <summary>Full path to this run's log file.</summary>
    public string FilePath { get; }

    /// <summary>Local time the run started.</summary>
    public DateTime StartedAt { get; }

    /// <summary>Records written so far, oldest first.</summary>
    public IReadOnlyList<ChangeRecord> Records => _records;

    /// <summary>
    /// Writes one change record to disk and returns it. Call this BEFORE applying the change:
    /// when this method returns, the undo path already exists on disk.
    /// </summary>
    /// <param name="module">Engine module that owns the change. Must match its undo handler.</param>
    /// <param name="action">What is being done, e.g. <c>SetActivePlan</c>.</param>
    /// <param name="target">What is being changed — a value name, service name, or path.</param>
    /// <param name="oldValue">The value read from the live system. <c>null</c> if the target does not exist.</param>
    /// <param name="newValue">The value about to be written. <c>null</c> if the target is being removed.</param>
    /// <param name="undoable">
    /// False only for genuinely permanent changes, such as deleting a temp file. The record is
    /// still written, so the user can see what was removed.
    /// </param>
    /// <exception cref="IOException">The record could not be written. The change must not be applied.</exception>
    public ChangeRecord RecordChange(
        string module,
        string action,
        string target,
        string? oldValue,
        string? newValue,
        bool undoable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var record = new ChangeRecord
        {
            Id = _idPrefix + _nextSequence.ToString("D3", CultureInfo.InvariantCulture),
            Time = TrimToSeconds(_time.GetLocalNow().DateTime),
            Module = module,
            Action = action,
            Target = target,
            OldValue = oldValue,
            NewValue = newValue,
            Undone = false,
            Undoable = undoable,
        };

        var line = JsonSerializer.Serialize(record, ChangeLogJson.Options);
        File.AppendAllText(FilePath, line + Environment.NewLine);

        _nextSequence++;
        _records.Add(record);
        return record;
    }

    /// <summary>The log format stores whole seconds, so drop anything finer before writing.</summary>
    private static DateTime TrimToSeconds(DateTime value)
        => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Kind);
}
