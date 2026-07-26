using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;

namespace SystevoTune.Engine.Tweaks.Power;

/// <summary>
/// Switches the active power scheme. Gaming wants High or Ultimate, Work wants Balanced (doc 3.4).
/// </summary>
public sealed class PowerPlanTweak(
    IPowerPlanService powerPlans,
    PowerPlanCatalog catalog,
    IBatteryStatus battery,
    string targetPlanId) : ITweak
{
    /// <summary>Module name on every power plan change record.</summary>
    public const string ModuleName = "PowerPlan";

    /// <summary>The change log target for the active scheme, matching doc 5.2's example.</summary>
    public const string TargetName = "ActivePowerScheme";

    /// <summary>Plans that draw more power, so switching to them on battery deserves a warning.</summary>
    private static readonly string[] ThirstyPlans = ["high-performance", "ultimate-performance"];

    /// <inheritdoc />
    public string Id { get; } = "power-plan." + targetPlanId;

    /// <inheritdoc />
    public string Name => "Power plan";

    /// <inheritdoc />
    public string Module => ModuleName;

    /// <inheritdoc />
    public async Task<TweakPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var target = catalog.Find(targetPlanId)
            ?? throw new InvalidOperationException($"'{targetPlanId}' is not in the power plan whitelist.");

        var available = await powerPlans.ListAsync(cancellationToken).ConfigureAwait(false);

        // Ultimate Performance is absent on most consumer installs. Not an error — just skip it.
        if (available.All(plan => plan.Id != target.Guid))
        {
            return TweakPlan.NotApplicable(Id, Name,
                $"{target.NameEn} is not available on this PC, so the power plan was left alone.");
        }

        var active = available.FirstOrDefault(plan => plan.IsActive)?.Id;
        if (active == target.Guid)
        {
            return TweakPlan.AlreadyApplied(Id, Name, $"The power plan is already {target.NameEn}.");
        }

        var change = new PlannedChange(
            ModuleName,
            "SetActivePlan",
            TargetName,
            active?.ToString("D"),
            target.Guid.ToString("D"),
            $"Power plan: {DescribePlan(active)} to {target.NameEn}.");

        return new TweakPlan(Id, Name, TweakStatus.Ready, [change], BatteryWarning(target));
    }

    /// <inheritdoc />
    public async Task ApplyChangeAsync(PlannedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!Guid.TryParse(change.NewValue, out var planId))
        {
            throw new InvalidOperationException($"'{change.NewValue}' is not a power scheme GUID.");
        }

        await powerPlans.SetActiveAsync(planId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Doc 3.4: warn before putting a laptop on a thirsty plan while it is unplugged.</summary>
    private string? BatteryWarning(PowerPlanEntry target)
    {
        if (battery.Current != BatteryState.OnBattery || !ThirstyPlans.Contains(target.Id, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"This PC is running on battery. {target.NameEn} will drain it faster.";
    }

    private string DescribePlan(Guid? planId)
    {
        if (planId is null)
        {
            return "unknown";
        }

        return catalog.Find(planId.Value)?.NameEn ?? planId.Value.ToString("D");
    }
}

/// <summary>Puts the previous power scheme back.</summary>
public sealed class PowerPlanUndoHandler(IPowerPlanService powerPlans) : IUndoHandler
{
    /// <inheritdoc />
    public string Module => PowerPlanTweak.ModuleName;

    /// <inheritdoc />
    public async Task UndoAsync(ChangeRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        // A run that could not read the old scheme wrote null. There is nothing to go back to,
        // and guessing a default would be worse than saying so.
        if (!Guid.TryParse(record.OldValue, out var previous))
        {
            throw new InvalidOperationException(
                "The previous power plan was not recorded, so it cannot be put back. Set it by hand in Power Options.");
        }

        await powerPlans.SetActiveAsync(previous, cancellationToken).ConfigureAwait(false);
    }
}
