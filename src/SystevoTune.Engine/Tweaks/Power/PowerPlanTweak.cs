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

    /// <summary>Action for switching the active scheme.</summary>
    public const string SetAction = "SetActivePlan";

    /// <summary>Action for creating a scheme this PC did not have. Undo deletes it again.</summary>
    public const string CreateAction = "CreateScheme";

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
        var active = available.FirstOrDefault(plan => plan.IsActive)?.Id;

        // Never assume a GUID exists (O1). Walk the preference list against what this PC really
        // has, matching by id and then by name.
        if (PowerPlanResolver.MatchFirst(available, wanted) is { } found)
        {
            return found.Plan.Id == active
                ? TweakPlan.AlreadyApplied(Id, Name, $"The power plan is already {found.Entry.NameEn}.")
                : ReadyPlan(found.Entry, found.Plan.Id, active, created: false);
        }

        // Nothing matched. Copying a template is the documented way to get a scheme a PC does not
        // list, and it is reversible because undo deletes the copy.
        var creatable = wanted.FirstOrDefault(entry => entry.CanCreate);
        if (creatable is not null)
        {
            return ReadyPlan(creatable, creatable.CreateAs!.Value, active, created: true);
        }

        return TweakPlan.NotApplicable(Id, Name,
            $"{string.Join(" and ", wanted.Select(plan => plan.NameEn))} "
            + $"could not be found and cannot be created — {PowerPlanResolver.DescribeAvailable(available)}. "
            + "The power plan was left alone.");
    }

    /// <summary>
    /// Builds the change list. Creating a scheme is its own record so undo can delete it: the
    /// destination id is fixed in the whitelist, which is what makes it nameable before it exists.
    /// </summary>
    private TweakPlan ReadyPlan(PowerPlanEntry entry, Guid targetId, Guid? active, bool created)
    {
        var changes = new List<PlannedChange>();

        if (created)
        {
            changes.Add(new PlannedChange(
                ModuleName,
                CreateAction,
                targetId.ToString("D"),
                null,
                entry.CreateFrom!.Value.ToString("D"),
                $"Power plan: create {entry.NameEn}, which this PC does not offer. Undo removes it again."));
        }

        changes.Add(new PlannedChange(
            ModuleName,
            SetAction,
            TargetName,
            active?.ToString("D"),
            targetId.ToString("D"),
            $"Power plan: {DescribePlan(active)} to {entry.NameEn}."));

        return new TweakPlan(Id, Name, TweakStatus.Ready, changes, BatteryWarning(entry));
    }

    /// <inheritdoc />
    public async Task ApplyChangeAsync(PlannedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Action == CreateAction)
        {
            var source = ParseGuid(change.NewValue, "template");
            var destination = ParseGuid(change.Target, "new scheme");

            if (!await _powerPlans.TryDuplicateSchemeAsync(source, destination, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Windows would not copy the power scheme, so it could not be created. "
                    + "The power plan was left as it was.");
            }

            return;
        }

        await _powerPlans.SetActiveAsync(ParseGuid(change.NewValue, "power scheme"), cancellationToken)
            .ConfigureAwait(false);
    }

    private static Guid ParseGuid(string? value, string what)
        => Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"'{value}' is not a {what} GUID.");

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

/// <summary>
/// Puts the power plan back: the previous scheme reactivated, and any scheme the engine created
/// removed again.
/// </summary>
/// <remarks>
/// Undo runs newest-first, so the switch-back happens before the delete. That ordering matters —
/// Windows will not delete the scheme that is currently active.
/// </remarks>
public sealed class PowerPlanUndoHandler(IPowerPlanService powerPlans) : IUndoHandler
{
    /// <inheritdoc />
    public string Module => PowerPlanTweak.ModuleName;

    /// <inheritdoc />
    public async Task UndoAsync(ChangeRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Action == PowerPlanTweak.CreateAction)
        {
            // Doc 07.2 compares the VM against its snapshot, so a scheme we invented has to go.
            if (!Guid.TryParse(record.Target, out var created))
            {
                throw new InvalidOperationException($"'{record.Target}' is not a power scheme GUID.");
            }

            await powerPlans.DeleteSchemeAsync(created, cancellationToken).ConfigureAwait(false);
            return;
        }

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
