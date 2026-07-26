namespace SystevoTune.Engine.Safety;

/// <summary>One record that could not be put back, and why.</summary>
/// <param name="RunId">Run the record belongs to.</param>
/// <param name="RecordId">The record's id.</param>
/// <param name="Record">The record, or <c>null</c> if the id matched nothing on disk.</param>
/// <param name="Reason">Message for the user. Already human-readable.</param>
public sealed record UndoFailure(string RunId, string RecordId, ChangeRecord? Record, string Reason);

/// <summary>
/// Result of an undo pass. Undo never stops at the first failure, so this always describes
/// the whole attempt: what went back, what did not, and whether the user cancelled.
/// </summary>
/// <param name="Undone">Records restored, in the order they were undone (newest change first).</param>
/// <param name="Failures">Records that could not be restored. Show all of these to the user.</param>
/// <param name="Cancelled">The pass stopped early because the token was cancelled.</param>
public sealed record UndoReport(
    IReadOnlyList<ChangeRecord> Undone,
    IReadOnlyList<UndoFailure> Failures,
    bool Cancelled = false)
{
    /// <summary>An empty report — nothing was left to undo.</summary>
    public static UndoReport Empty { get; } = new([], []);

    /// <summary>Everything that was attempted went back, and nothing was skipped.</summary>
    public bool AllSucceeded => Failures.Count == 0 && !Cancelled;

    /// <summary>How many records the pass tried to restore.</summary>
    public int AttemptedCount => Undone.Count + Failures.Count;
}
