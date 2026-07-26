using SystevoTune.Engine.Tweaks;

namespace SystevoTune.Engine.Cleanup;

/// <summary>
/// Cleaning one whitelist group. One change per group rather than per file: doc 3.1 shows the
/// user size per group, and a 10,000-line preview helps nobody.
/// </summary>
public sealed class CleanupTweak(CleanupModule module, CleanupGroup group) : ITweak
{
    /// <inheritdoc />
    public string Id { get; } = CleanupModule.TweakIdPrefix + group.Id;

    /// <inheritdoc />
    public string Name { get; } = group.NameEn;

    /// <inheritdoc />
    public string Module => CleanupModule.ModuleName;

    /// <summary>What the last apply actually removed. <c>null</c> until applied.</summary>
    public CleanupApplyDetail? LastApply { get; private set; }

    /// <inheritdoc />
    public Task<TweakPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var scan = module.ScanGroup(group, cancellationToken);

        if (scan.RejectedPaths.Count > 0)
        {
            return Task.FromResult(TweakPlan.Blocked(Id, Name,
                "The whitelist names a path cleanup is not allowed to touch: "
                + string.Join("; ", scan.RejectedPaths)));
        }

        if (scan.FileCount == 0)
        {
            return Task.FromResult(TweakPlan.AlreadyApplied(Id, Name, $"{group.NameEn}: nothing to clean."));
        }

        var services = group.StopServices.Count == 0
            ? string.Empty
            : $" {string.Join(" and ", group.StopServices)} will be stopped first and started again afterwards;"
              + " if they will not stop, nothing is deleted.";

        var change = new PlannedChange(
            Module,
            "DeleteGroupContents",
            group.Id,
            CleanupModule.DescribeState(scan.FileCount, scan.TotalBytes),
            CleanupModule.DescribeState(0, 0),
            $"{group.NameEn}: delete {scan.FileCount} files, freeing about {scan.HumanSize}.{services}",
            Undoable: false);

        return Task.FromResult(TweakPlan.Ready(Id, Name, [change]));
    }

    /// <inheritdoc />
    public async Task ApplyChangeAsync(PlannedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        LastApply = await module.DeleteGroupAsync(group, cancellationToken).ConfigureAwait(false);
    }
}
