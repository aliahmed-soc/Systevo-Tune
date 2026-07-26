using SystevoTune.Engine.Safety;

namespace SystevoTune.Engine.Tweaks;

/// <summary>
/// Runs tweaks. Preview reads and reports; apply writes the log record for each change and only
/// then lets the tweak touch the system.
/// </summary>
/// <remarks>
/// This class is the reason "log first, change second" holds: <see cref="ITweak"/> exposes no way
/// to apply a change that has not come from a plan, and the only caller of
/// <see cref="ITweak.ApplyChangeAsync"/> is <see cref="ApplyAsync"/>, which records first.
/// </remarks>
public sealed class TweakRunner
{
    /// <summary>
    /// The dry run. Asks every tweak what it would change and returns the lot. Writes nothing,
    /// opens no log run, and touches nothing.
    /// </summary>
    public async Task<PreviewReport> PreviewAsync(
        IEnumerable<ITweak> tweaks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tweaks);

        var plans = new List<TweakPlan>();
        foreach (var tweak in tweaks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plans.Add(await PlanSafelyAsync(tweak, cancellationToken).ConfigureAwait(false));
        }

        return new PreviewReport(plans);
    }

    /// <summary>
    /// Applies every tweak into an open log run. Each tweak is re-planned first, so old values
    /// come from the live system rather than from a preview the user may have left on screen.
    /// One failure never stops the rest.
    /// </summary>
    public async Task<ApplyReport> ApplyAsync(
        IEnumerable<ITweak> tweaks,
        ChangeLogRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tweaks);
        ArgumentNullException.ThrowIfNull(run);

        var outcomes = new List<TweakOutcome>();
        var cancelled = false;

        foreach (var tweak in tweaks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var plan = await PlanSafelyAsync(tweak, cancellationToken).ConfigureAwait(false);

            if (!plan.HasChanges)
            {
                outcomes.Add(new TweakOutcome(plan.TweakId, plan.TweakName, plan.Status, [], [], plan.Message));
                continue;
            }

            var (applied, failures, stopped) = await ApplyPlanAsync(tweak, plan, run, cancellationToken)
                .ConfigureAwait(false);

            outcomes.Add(new TweakOutcome(
                plan.TweakId, plan.TweakName, plan.Status, applied, failures, plan.Message, plan.RequiresRestart));

            if (stopped)
            {
                cancelled = true;
                break;
            }
        }

        return new ApplyReport(run.RunId, outcomes, cancelled);
    }

    private static async Task<(List<ChangeRecord> Applied, List<TweakFailure> Failures, bool Cancelled)>
        ApplyPlanAsync(ITweak tweak, TweakPlan plan, ChangeLogRun run, CancellationToken cancellationToken)
    {
        var applied = new List<ChangeRecord>();
        var failures = new List<TweakFailure>();

        foreach (var change in plan.Changes)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return (applied, failures, true);
            }

            // Log first. If this throws, the change does not run — which is the point.
            ChangeRecord record;
            try
            {
                record = run.RecordChange(
                    change.Module, change.Action, change.Target, change.OldValue, change.NewValue, change.Undoable);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new TweakFailure(plan.TweakId, change,
                    $"Could not write the change log, so the change was not made: {ex.Message}"));
                continue;
            }

            // Change second.
            try
            {
                await tweak.ApplyChangeAsync(change, cancellationToken).ConfigureAwait(false);
                applied.Add(record);
            }
            catch (OperationCanceledException)
            {
                return (applied, failures, true);
            }
            catch (Exception ex)
            {
                // The record stays in the log even though the change failed. Undo will try to
                // restore a value that is already correct, which is harmless, and the record is
                // the only evidence the attempt happened.
                failures.Add(new TweakFailure(plan.TweakId, change, ex.Message));
            }
        }

        return (applied, failures, false);
    }

    /// <summary>A tweak that throws while looking at the system is Blocked, not fatal.</summary>
    private static async Task<TweakPlan> PlanSafelyAsync(ITweak tweak, CancellationToken cancellationToken)
    {
        try
        {
            return await tweak.PlanAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TweakPlan.Blocked(tweak.Id, tweak.Name, $"Could not read the current setting: {ex.Message}");
        }
    }
}
