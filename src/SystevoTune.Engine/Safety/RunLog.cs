namespace SystevoTune.Engine.Safety;

/// <summary>One run's change log, read back from disk.</summary>
/// <param name="RunId">Run identifier, also the file name without prefix or extension.</param>
/// <param name="FilePath">Full path to the log file.</param>
/// <param name="Records">Records in the order they were written — oldest first.</param>
/// <param name="SkippedLineCount">
/// Lines that could not be parsed. Normally zero. One skipped line at the end of a file is the
/// signature of a crash mid-write: the change may or may not have been applied.
/// </param>
public sealed record RunLog(
    string RunId,
    string FilePath,
    IReadOnlyList<ChangeRecord> Records,
    int SkippedLineCount);
