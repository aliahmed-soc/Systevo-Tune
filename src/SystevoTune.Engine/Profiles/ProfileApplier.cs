using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.Engine.Profiles;

/// <summary>What an apply produced: the report, and the tweak objects that made it.</summary>
/// <param name="Report">How the run went.</param>
/// <param name="Tweaks">
/// The same instances the run used. Callers need these for per-tweak detail such as
/// <see cref="Cleanup.CleanupTweak.LastApply"/> — rebuilding the profile would hand back fresh
/// objects with nothing recorded on them.
/// </param>
public sealed record ProfileApplyResult(ApplyReport Report, IReadOnlyList<ITweak> Tweaks);

/// <summary>
/// Applies a profile and notes which one it was, so it can be re-applied later.
/// </summary>
/// <remarks>
/// The marker is written here rather than left to callers. Doc 5.6 wants a "re-apply last
/// profile" button, and that only works if every profile run records its identity — a caller that
/// forgets would silently make the run un-repeatable.
/// </remarks>
public sealed class ProfileApplier(ProfileBuilder builder, TweakRunner runner)
{
    /// <summary>Applies a profile into an open run.</summary>
    /// <param name="profile">The preset to apply.</param>
    /// <param name="run">The open log run to record into.</param>
    /// <param name="progress">Reported after each tweak, so a UI can show results as they happen.</param>
    /// <param name="cancellationToken">Stops the run between tweaks.</param>
    public async Task<ProfileApplyResult> ApplyAsync(
        Profile profile,
        ChangeLogRun run,
        IProgress<TweakOutcome>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(run);

        run.RecordProfile(profile.Id);

        var tweaks = builder.Build(profile);
        var report = await runner.ApplyAsync(tweaks, run, progress, cancellationToken).ConfigureAwait(false);

        return new ProfileApplyResult(report, tweaks);
    }
}

/// <summary>A profile run that could be repeated.</summary>
/// <param name="ProfileId">The profile that was applied.</param>
/// <param name="RunId">The run it was applied in.</param>
/// <param name="AppliedAt">When.</param>
/// <param name="ChangeCount">How many changes that run made.</param>
public sealed record ReapplyTarget(string ProfileId, string RunId, DateTime AppliedAt, int ChangeCount);

/// <summary>
/// Finds the last profile that was applied, for doc 5.6's "re-apply after a Windows update".
/// </summary>
/// <remarks>
/// Re-applying is a fresh run, not a replay of old records. Every tweak re-plans against the
/// live system, so anything Windows left alone is reported as already applied and only what
/// actually got reset is written again — and the new run gets its own undo path.
/// </remarks>
public sealed class ReapplyService(ChangeLog log, ProfileCatalog profiles)
{
    /// <summary>
    /// The most recent run that applied a profile still present in the catalogue, or <c>null</c>.
    /// </summary>
    public ReapplyTarget? FindLast()
    {
        // ReadAllRuns is newest first, so the first hit is the most recent.
        foreach (var run in log.ReadAllRuns())
        {
            if (run.ProfileId is not { } profileId || profiles.Find(profileId) is null)
            {
                continue;
            }

            return new ReapplyTarget(
                profileId,
                run.RunId,
                run.Records[0].Time,
                run.Changes.Count);
        }

        return null;
    }

    /// <summary>The profile named by <see cref="FindLast"/>, or <c>null</c>.</summary>
    public Profile? FindLastProfile()
        => FindLast() is { } target ? profiles.Find(target.ProfileId) : null;
}
