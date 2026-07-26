using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;

namespace SystevoTune.Engine.Tweaks.Power;

/// <summary>
/// Switches the active power scheme. Gaming wants High or Ultimate, Work wants Balanced (doc 3.4).
/// </summary>
public sealed class PowerPlanTweak : ITweak
{
    /// <summary>Module name on every power plan change record.</summary>
    public const string ModuleName = "PowerPlan";

    /// <summary>The change log target for the active scheme, matching doc 5.2's example.</summary>
    public const string TargetName = "ActivePowerScheme";

    /// <summary>Plans that draw more power, so switching to them on battery deserves a warning.</summary>
    private static readonly string[] ThirstyPlans = ["high-performance", "ultimate-performance"];

    private readonly IPowerPlanService _powerPlans;
    private readonly PowerPlanCatalog _catalog;
    private readonly IBatteryStatus _battery;
    private readonly IReadOnlyList<string> _preferredPlanIds;

    /// <summary>Switches to one specific scheme.</summary>
    public PowerPlanTweak(
        IPowerPlanService powerPlans,
        PowerPlanCatalog catalog,
        IBatteryStatus battery,
        string targetPlanId)
        : this(powerPlans, catalog, battery, [targetPlanId])
    {
    }

    /// <summary>
    /// Switches to the first scheme in <paramref name="preferredPlanIds"/> that this PC actually
    /// has. Doc 3.4 wants "High Performance, or Ultimate Performance if present", which is this
    /// list in reverse preference order.
    /// </summary>
    public PowerPlanTweak(
        IPowerPlanService powerPlans,
        PowerPlanCatalog catalog,
        IBatteryStatus battery,
        IReadOnlyList<string> preferredPlanIds)
    {
        ArgumentNullException.ThrowIfNull(preferredPlanIds);
        if (preferredPlanIds.Count == 0)
        {
            throw new ArgumentException("A power plan tweak needs at least one plan id.", nameof(preferredPlanIds));
        }

        _powerPlans = powerPlans;
        _catalog = catalog;
        _battery = battery;
        _preferredPlanIds = preferredPlanIds;
        Id = "power-plan." + preferredPlanIds[0];
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string Name => "Power plan";

    /// <inheritdoc />
    public string Module => ModuleName;

    /// <inheritdoc />
    public async Task<TweakPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var wanted = _preferredPlanIds
            .Select(id => _catalog.Find(id)
                ?? throw new InvalidOperationException($"'{id}' is not in the power plan whitelist."))
            .ToList();

        var available = await _powerPlans.ListAsync(cancellationToken).ConfigureAwait(false);

        // Ultimate Performance is absent on most consumer installs, so fall through to the next
        // choice rather than treating it as an error.
        var target = wanted.FirstOrDefault(plan => available.Any(candidate => candidate.Id == plan.Guid));
        if (target is null)
        {
            return TweakPlan.NotApplicable(Id, Name,
                $"{string.Join(" and ", wanted.Select(plan => plan.NameEn))} "
                + "are not available on this PC, so the power plan was left alone.");
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

        await _powerPlans.SetActiveAsync(planId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Doc 3.4: warn before putting a laptop on a thirsty plan while it is unplugged.</summary>
    private string? BatteryWarning(PowerPlanEntry target)
    {
        if (_battery.Current != BatteryState.OnBattery || !ThirstyPlans.Contains(target.Id, StringComparer.OrdinalIgnoreCase))
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

        return _catalog.Find(planId.Value)?.NameEn ?? planId.Value.ToString("D");
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
