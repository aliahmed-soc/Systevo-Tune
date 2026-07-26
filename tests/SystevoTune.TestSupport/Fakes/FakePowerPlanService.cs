using SystevoTune.Engine.Platform;

namespace SystevoTune.TestSupport;

/// <summary>In-memory power schemes. No unit test runs powercfg.</summary>
public sealed class FakePowerPlanService : IPowerPlanService
{
    private readonly List<PowerPlan> _plans = [];

    /// <summary>Schemes this fake was asked to activate, in order.</summary>
    public List<Guid> Activated { get; } = [];

    /// <summary>When set, <see cref="SetActiveAsync"/> throws it.</summary>
    public Exception? SetFailure { get; set; }

    /// <summary>Adds a scheme.</summary>
    public FakePowerPlanService With(Guid id, string name, bool isActive = false)
    {
        _plans.Add(new PowerPlan(id, name, isActive));
        return this;
    }

    /// <summary>The scheme currently marked active.</summary>
    public Guid? Active => _plans.FirstOrDefault(plan => plan.IsActive)?.Id;

    public Task<IReadOnlyList<PowerPlan>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PowerPlan>>(_plans);

    public Task<Guid?> GetActiveAsync(CancellationToken cancellationToken) => Task.FromResult(Active);

    public Task SetActiveAsync(Guid planId, CancellationToken cancellationToken)
    {
        if (SetFailure is not null)
        {
            return Task.FromException(SetFailure);
        }

        // powercfg refuses a scheme that is not there, so the fake does too. Without this a bug
        // that activates a scheme we failed to create would pass silently.
        if (_plans.All(plan => plan.Id != planId))
        {
            return Task.FromException(
                new InvalidOperationException($"There is no power scheme {planId:D} on this PC."));
        }

        Activated.Add(planId);

        for (var i = 0; i < _plans.Count; i++)
        {
            _plans[i] = _plans[i] with { IsActive = _plans[i].Id == planId };
        }

        return Task.CompletedTask;
    }

    /// <summary>Templates this fake will copy. Anything else is refused, as Windows would.</summary>
    public HashSet<Guid> DuplicableTemplates { get; } = [];

    /// <summary>Schemes created through <see cref="TryDuplicateSchemeAsync"/>, in order.</summary>
    public List<Guid> Created { get; } = [];

    /// <summary>Schemes removed through <see cref="DeleteSchemeAsync"/>, in order.</summary>
    public List<Guid> Deleted { get; } = [];

    public Task<bool> TryDuplicateSchemeAsync(Guid source, Guid destination, CancellationToken cancellationToken)
    {
        if (!DuplicableTemplates.Contains(source))
        {
            return Task.FromResult(false);
        }

        _plans.Add(new PowerPlan(destination, "Copied scheme", IsActive: false));
        Created.Add(destination);
        return Task.FromResult(true);
    }

    public Task DeleteSchemeAsync(Guid planId, CancellationToken cancellationToken)
    {
        if (Active == planId)
        {
            // Windows refuses this, and undo ordering is what keeps us out of the situation.
            throw new InvalidOperationException("The active power scheme cannot be deleted.");
        }

        _plans.RemoveAll(plan => plan.Id == planId);
        Deleted.Add(planId);
        return Task.CompletedTask;
    }
}

/// <summary>A battery state a test can set.</summary>
public sealed class FakeBatteryStatus(BatteryState state = BatteryState.NoBattery) : IBatteryStatus
{
    public BatteryState Current { get; set; } = state;
}
