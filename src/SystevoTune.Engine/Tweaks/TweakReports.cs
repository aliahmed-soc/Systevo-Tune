using SystevoTune.Engine.Safety;

namespace SystevoTune.Engine.Tweaks;

/// <summary>A planned change that could not be applied, and why.</summary>
public sealed record TweakFailure(string TweakId, PlannedChange Change, string Reason);

/// <summary>
/// The dry run: every tweak's plan, nothing touched. This is what doc 5.5 puts in front of the
/// user before the second click.
/// </summary>
public sealed record PreviewReport(IReadOnlyList<TweakPlan> Plans)
{
    /// <summary>Every change that would be made, across all tweaks.</summary>
    public IReadOnlyList<PlannedChange> AllChanges { get; } =
        Plans.Where(plan => plan.HasChanges).SelectMany(plan => plan.Changes).ToList();

    /// <summary>Tweaks that would actually do something.</summary>
    public IReadOnlyList<TweakPlan> Actionable { get; } = Plans.Where(plan => plan.HasChanges).ToList();

    /// <summary>Whether applying this preview would need a restart to take full effect.</summary>
    public bool RequiresRestart => Plans.Any(plan => plan.HasChanges && plan.RequiresRestart);
}

/// <summary>How one tweak's apply went.</summary>
public sealed record TweakOutcome(
    string TweakId,
    string TweakName,
    TweakStatus Status,
    IReadOnlyList<ChangeRecord> Applied,
    IReadOnlyList<TweakFailure> Failures,
    string? Message = null,
    bool RequiresRestart = false)
{
    /// <summary>The tweak did everything it planned.</summary>
    public bool Succeeded => Failures.Count == 0;
}

/// <summary>
/// How an apply run went. Like undo, apply never stops at the first failure, so this always
/// describes the whole run.
/// </summary>
public sealed record ApplyReport(
    string RunId,
    IReadOnlyList<TweakOutcome> Outcomes,
    bool Cancelled = false)
{
    /// <summary>Every record written and applied, oldest first.</summary>
    public IReadOnlyList<ChangeRecord> AllApplied { get; } =
        Outcomes.SelectMany(outcome => outcome.Applied).ToList();

    /// <summary>Everything that went wrong. Show all of it.</summary>
    public IReadOnlyList<TweakFailure> AllFailures { get; } =
        Outcomes.SelectMany(outcome => outcome.Failures).ToList();

    /// <summary>A restart is needed before some of these changes take full effect.</summary>
    public bool RequiresRestart => Outcomes.Any(outcome => outcome.RequiresRestart && outcome.Applied.Count > 0);

    /// <summary>Nothing failed and nothing was cut short.</summary>
    public bool AllSucceeded => AllFailures.Count == 0 && !Cancelled;
}
