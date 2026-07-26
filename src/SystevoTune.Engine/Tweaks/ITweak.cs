namespace SystevoTune.Engine.Tweaks;

/// <summary>What a tweak found when it looked at the system.</summary>
public enum TweakStatus
{
    /// <summary>There is something to change.</summary>
    Ready,

    /// <summary>The system is already how the tweak wants it. Nothing to do.</summary>
    AlreadyApplied,

    /// <summary>This PC does not have what the tweak needs — an absent power plan, an old Windows build.</summary>
    NotApplicable,

    /// <summary>The tweak could not even look. Not elevated, key unreadable, whitelist missing.</summary>
    Blocked,
}

/// <summary>
/// One intended change, old value to new value. Produced by preview, consumed by the runner.
/// Carries everything the change log needs, so preview output and log records cannot drift apart.
/// </summary>
/// <param name="Module">Routes the undo. Must match an <see cref="Safety.IUndoHandler"/>.</param>
/// <param name="Action">What is being done, e.g. <c>SetDwordValue</c>.</param>
/// <param name="Target">What is being changed, in a form the module's undo handler can parse.</param>
/// <param name="OldValue">Read from the live system. <c>null</c> means it does not exist yet.</param>
/// <param name="NewValue">What will be written. <c>null</c> means the target is removed.</param>
/// <param name="Description">One line for the user, already readable.</param>
/// <param name="Undoable">False only for permanent changes such as deleting a temp file.</param>
public sealed record PlannedChange(
    string Module,
    string Action,
    string Target,
    string? OldValue,
    string? NewValue,
    string Description,
    bool Undoable = true);

/// <summary>
/// A tweak's preview: what it would change, or why it would not. Doc 5.5 — the user sees this
/// list before anything happens, and applying is a separate decision.
/// </summary>
public sealed record TweakPlan(
    string TweakId,
    string TweakName,
    TweakStatus Status,
    IReadOnlyList<PlannedChange> Changes,
    string? Message = null,
    bool RequiresRestart = false)
{
    /// <summary>There is work to do.</summary>
    public bool HasChanges => Status is TweakStatus.Ready && Changes.Count > 0;

    /// <summary>A plan with changes to make.</summary>
    public static TweakPlan Ready(
        string tweakId,
        string tweakName,
        IReadOnlyList<PlannedChange> changes,
        bool requiresRestart = false)
        => new(tweakId, tweakName, TweakStatus.Ready, changes, RequiresRestart: requiresRestart);

    /// <summary>Nothing to do — the system already matches.</summary>
    public static TweakPlan AlreadyApplied(string tweakId, string tweakName, string message)
        => new(tweakId, tweakName, TweakStatus.AlreadyApplied, [], message);

    /// <summary>This PC cannot take the tweak.</summary>
    public static TweakPlan NotApplicable(string tweakId, string tweakName, string message)
        => new(tweakId, tweakName, TweakStatus.NotApplicable, [], message);

    /// <summary>The tweak could not read what it needed.</summary>
    public static TweakPlan Blocked(string tweakId, string tweakName, string message)
        => new(tweakId, tweakName, TweakStatus.Blocked, [], message);
}

/// <summary>
/// One system change the engine can make and put back.
/// </summary>
/// <remarks>
/// The split is deliberate. <see cref="PlanAsync"/> only reads — that is preview, and running it
/// alone is a complete dry run. <see cref="ApplyChangeAsync"/> applies exactly one already-planned
/// change and is only ever called by <see cref="TweakRunner"/>, which writes the log record first.
/// A tweak therefore has no way to change the system without a log entry already on disk.
/// </remarks>
public interface ITweak
{
    /// <summary>Stable id used in profile files. Never change it once shipped.</summary>
    string Id { get; }

    /// <summary>Short name for the user.</summary>
    string Name { get; }

    /// <summary>Which undo handler owns this tweak's records.</summary>
    string Module { get; }

    /// <summary>
    /// Looks at the system and reports what would change. Must not change anything.
    /// Called again at apply time so old values are read fresh rather than trusted from preview.
    /// </summary>
    Task<TweakPlan> PlanAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies one change from a plan. Throw to report failure — the runner records it and
    /// carries on with the remaining changes.
    /// </summary>
    Task ApplyChangeAsync(PlannedChange change, CancellationToken cancellationToken);
}
