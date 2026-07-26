namespace SystevoTune.Engine.Safety;

/// <summary>
/// Puts one module's change back. Every module that writes change records must register one,
/// or its records become un-undoable — which the undo engine reports as a failure.
/// </summary>
public interface IUndoHandler
{
    /// <summary>Must match <see cref="ChangeRecord.Module"/> exactly. Matched case-insensitively.</summary>
    string Module { get; }

    /// <summary>
    /// Restores <see cref="ChangeRecord.OldValue"/> at <see cref="ChangeRecord.Target"/>.
    /// Throw to report failure — the undo engine catches it, records the reason, and carries on
    /// with the remaining records.
    /// </summary>
    Task UndoAsync(ChangeRecord record, CancellationToken cancellationToken);
}
