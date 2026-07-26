using SystevoTune.Engine.Tweaks;

namespace SystevoTune.TestSupport;

/// <summary>
/// A tweak with no system behind it. Records what it was asked to apply, and can be told to
/// fail at plan time or on a named target.
/// </summary>
public sealed class FakeTweak(string id, string module = "Fake") : ITweak
{
    private readonly List<PlannedChange> _applied = [];

    public string Id { get; } = id;

    public string Name { get; init; } = id;

    public string Module { get; } = module;

    /// <summary>What the tweak reports from <see cref="PlanAsync"/>.</summary>
    public TweakPlan Plan { get; set; } = TweakPlan.AlreadyApplied(id, id, "nothing to do");

    /// <summary>Thrown from <see cref="PlanAsync"/> when set.</summary>
    public Exception? PlanFailure { get; set; }

    /// <summary>Targets whose apply throws.</summary>
    public HashSet<string> FailingTargets { get; } = new(StringComparer.Ordinal);

    /// <summary>Runs on every apply, before the change is recorded as applied.</summary>
    public Action<PlannedChange>? OnApply { get; set; }

    /// <summary>Changes this tweak actually applied, in order.</summary>
    public IReadOnlyList<PlannedChange> Applied => _applied;

    /// <summary>How many times the tweak was asked to plan.</summary>
    public int PlanCount { get; private set; }

    /// <summary>Gives the tweak a single ready change.</summary>
    public FakeTweak Changing(string target, string? oldValue, string? newValue, bool undoable = true)
    {
        Plan = TweakPlan.Ready(Id, Name,
            [new PlannedChange(Module, "SetValue", target, oldValue, newValue, $"{target}: {oldValue} to {newValue}", undoable)]);
        return this;
    }

    public Task<TweakPlan> PlanAsync(CancellationToken cancellationToken)
    {
        PlanCount++;

        return PlanFailure is not null
            ? Task.FromException<TweakPlan>(PlanFailure)
            : Task.FromResult(Plan);
    }

    public Task ApplyChangeAsync(PlannedChange change, CancellationToken cancellationToken)
    {
        OnApply?.Invoke(change);

        if (FailingTargets.Contains(change.Target))
        {
            throw new InvalidOperationException($"'{change.Target}' refused the write.");
        }

        _applied.Add(change);
        return Task.CompletedTask;
    }
}
