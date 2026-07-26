using System.Globalization;
using System.Text.Json;

namespace SystevoTune.Engine.Safety;

/// <summary>
/// The change log: one file per run, one JSON record per line.
/// This is the single source of truth for undo — nothing else stores old values.
/// </summary>
/// <remarks>
/// Line-per-record (JSONL) rather than a JSON array on purpose. Doc 05 requires the record to
/// reach disk before the change runs and to survive a crash mid-change. Appending a line does
/// both; appending inside an array does not.
/// </remarks>
public sealed class ChangeLog
{
    private const string FilePrefix = "run-";
    private const string FileExtension = ".jsonl";
    private const string TempExtension = ".tmp";

    private readonly TimeProvider _time;

    /// <param name="logDirectory">Where run files live. Created on first write.</param>
    /// <param name="timeProvider">Injected so tests are deterministic. Defaults to the system clock.</param>
    public ChangeLog(string logDirectory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        DirectoryPath = logDirectory;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Where this instance reads and writes run files.</summary>
    public string DirectoryPath { get; }

    /// <summary>The shipped location: <c>C:\ProgramData\SystevoTune\logs</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        EngineInfo.ProductName,
        "logs");

    /// <summary>A change log pointed at <see cref="DefaultDirectory"/>.</summary>
    public static ChangeLog Default(TimeProvider? timeProvider = null) => new(DefaultDirectory, timeProvider);

    /// <summary>
    /// Opens a new run and creates its (empty) log file. Record ids continue the sequence
    /// already used today, so two runs on one day never share an id.
    /// </summary>
    public ChangeLogRun StartRun()
    {
        Directory.CreateDirectory(DirectoryPath);

        var startedAt = _time.GetLocalNow().DateTime;
        var nextSequence = NextSequenceForDay(startedAt);
        var runId = NextRunId(startedAt);
        var path = PathForRun(runId);

        File.WriteAllText(path, string.Empty);

        return new ChangeLogRun(runId, path, startedAt, nextSequence, _time);
    }

    /// <summary>Run ids on disk, newest first.</summary>
    public IReadOnlyList<string> ListRunIds()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(DirectoryPath, FilePrefix + "*" + FileExtension)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name![FilePrefix.Length..])
            .OrderByDescending(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads one run. Unparseable lines are skipped and counted, never thrown on.</summary>
    /// <exception cref="FileNotFoundException">No log file for that run id.</exception>
    public RunLog ReadRun(string runId)
    {
        var path = PathForRun(runId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No change log for run '{runId}'.", path);
        }

        var records = new List<ChangeRecord>();
        var skipped = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParse(line, out var record))
            {
                records.Add(record);
            }
            else
            {
                skipped++;
            }
        }

        return new RunLog(runId, path, records, skipped);
    }

    /// <summary>Every run on disk, newest first.</summary>
    public IReadOnlyList<RunLog> ReadAllRuns() => ListRunIds().Select(ReadRun).ToList();

    /// <summary>
    /// Marks one record undone and rewrites the run file. Called only after the old value is
    /// actually back in place, so a crash can at worst repeat an undo — which is harmless,
    /// because undo restores an absolute value rather than reversing a delta.
    /// </summary>
    /// <returns><c>true</c> if the record was found and updated.</returns>
    public bool MarkUndone(string runId, string recordId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        var path = PathForRun(runId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No change log for run '{runId}'.", path);
        }

        var lines = File.ReadAllLines(path);
        var found = false;

        for (var i = 0; i < lines.Length; i++)
        {
            // Lines that do not parse are left exactly as they are: a torn line is still
            // evidence of what the engine was doing when it died.
            if (!TryParse(lines[i], out var record) || record.Id != recordId)
            {
                continue;
            }

            lines[i] = JsonSerializer.Serialize(record with { Undone = true }, ChangeLogJson.Options);
            found = true;
        }

        if (!found)
        {
            return false;
        }

        // Write beside the log then swap, so a crash never leaves a half-written log.
        var temporaryPath = path + TempExtension;
        File.WriteAllLines(temporaryPath, lines);
        File.Move(temporaryPath, path, overwrite: true);
        return true;
    }

    private static bool TryParse(string line, out ChangeRecord record)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<ChangeRecord>(line, ChangeLogJson.Options);
            record = parsed!;
            return parsed is not null;
        }
        catch (JsonException)
        {
            record = null!;
            return false;
        }
    }

    private string PathForRun(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (runId.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"'{runId}' is not a valid run id.", nameof(runId));
        }

        return Path.Combine(DirectoryPath, FilePrefix + runId + FileExtension);
    }

    /// <summary>
    /// Builds a run id from the clock, adding a counter if a run already started this second.
    /// Ids sort lexicographically in chronological order, which is what <see cref="ListRunIds"/> relies on.
    /// </summary>
    private string NextRunId(DateTime startedAt)
    {
        var baseId = startedAt.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        if (!File.Exists(PathForRun(baseId)))
        {
            return baseId;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = baseId + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            if (!File.Exists(PathForRun(candidate)))
            {
                return candidate;
            }
        }
    }

    /// <summary>Continues today's id sequence so a second run cannot reuse a first run's ids.</summary>
    private int NextSequenceForDay(DateTime day)
    {
        var prefix = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-";
        var highest = 0;

        foreach (var run in ReadAllRuns())
        {
            foreach (var record in run.Records)
            {
                if (!record.Id.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (int.TryParse(record.Id.AsSpan(prefix.Length), NumberStyles.None,
                        CultureInfo.InvariantCulture, out var sequence) && sequence > highest)
                {
                    highest = sequence;
                }
            }
        }

        return highest + 1;
    }
}
