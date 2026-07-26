using SystevoTune.Engine.Platform;

namespace SystevoTune.Engine.Tests.Fakes;

/// <summary>In-memory power schemes. No unit test runs powercfg.</summary>
internal sealed class FakePowerPlanService : IPowerPlanService
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

        Activated.Add(planId);

        for (var i = 0; i < _plans.Count; i++)
        {
            _plans[i] = _plans[i] with { IsActive = _plans[i].Id == planId };
        }

        return Task.CompletedTask;
    }
}

/// <summary>A battery state a test can set.</summary>
internal sealed class FakeBatteryStatus(BatteryState state = BatteryState.NoBattery) : IBatteryStatus
{
    public BatteryState Current { get; set; } = state;
}
