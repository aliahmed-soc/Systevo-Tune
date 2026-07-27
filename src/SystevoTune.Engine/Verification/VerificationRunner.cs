using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.Engine.Verification;

/// <summary>The full result of a verification run.</summary>
/// <param name="ProfileId">Profile that was applied.</param>
/// <param name="RunId">The change log run it used.</param>
/// <param name="Before">State before anything happened.</param>
/// <param name="AfterApply">State once the profile was applied.</param>
/// <param name="AfterUndo">State once Undo All had run.</param>
/// <param name="Apply">How the apply went.</param>
/// <param name="Undo">How the undo went.</param>
/// <param name="Differences">
/// Everything still different between <paramref name="Before"/> and <paramref name="AfterUndo"/>.
/// Doc 07.2: any difference is a bug.
/// </param>
public sealed record VerificationReport(
    string ProfileId,
    string RunId,
    SystemStateSnapshot Before,
    SystemStateSnapshot AfterApply,
    SystemStateSnapshot AfterUndo,
    ApplyReport Apply,
    UndoReport Undo,
    IReadOnlyList<StateDifference> Differences)
{
    /// <summary>What the profile actually changed, for context on the result.</summary>
    public IReadOnlyList<StateDifference> AppliedChanges { get; } = StateDiff.Compare(Before, AfterApply);

    /// <summary>
    /// The PC came back to where it started. This is doc 07.2's pass condition.
    /// </summary>
    /// <remarks>
    /// Deliberately does not require the apply or undo reports to be clean. A tweak that was
    /// NotApplicable, or a locked file, does not make the machine dirty — the only question this
    /// property answers is whether anything was left changed.
    /// </remarks>
    public bool ReturnedToStart => Differences.Count == 0;

    /// <summary>
    /// The profile did something worth undoing. A run where nothing applied proves nothing, and
    /// a green result from it would be a false pass.
    /// </summary>
    public bool ProvedAnything => AppliedChanges.Count > 0;
}

/// <summary>
/// Doc 07.2's key test, run by the machine: snapshot, apply, snapshot, undo all, snapshot,
/// compare the first and last.
/// </summary>
/// <remarks>
/// This is engine code with no console in it — it returns a report and the caller decides how to
/// show it. It changes the system, so it only ever runs inside the VM, behind the ConsoleRunner's
/// VM interlock.
/// </remarks>
public sealed class VerificationRunner(
    SystemStateCollector collector,
    ProfileApplier applier,
    ChangeLog log,
    UndoEngine undo)
{
    /// <summary>Runs the whole cycle.</summary>
    public async Task<VerificationReport> RunAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var before = await collector.CaptureAsync("before", cancellationToken).ConfigureAwait(false);

        var run = log.StartRun();
        var applied = await applier.ApplyAsync(profile, run, progress: null, cancellationToken).ConfigureAwait(false);

        var afterApply = await collector.CaptureAsync("after-apply", cancellationToken).ConfigureAwait(false);

        var undoReport = await undo.UndoAllAsync(cancellationToken).ConfigureAwait(false);

        var afterUndo = await collector.CaptureAsync("after-undo", cancellationToken).ConfigureAwait(false);

        return new VerificationReport(
            profile.Id,
            run.RunId,
            before,
            afterApply,
            afterUndo,
            applied.Report,
            undoReport,
            StateDiff.Compare(before, afterUndo));
    }
}
