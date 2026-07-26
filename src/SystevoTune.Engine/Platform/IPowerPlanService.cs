namespace SystevoTune.Engine.Platform;

/// <summary>A power scheme as Windows reports it.</summary>
public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>Whether the PC is running on its battery.</summary>
public enum BatteryState
{
    /// <summary>Could not tell.</summary>
    Unknown,

    /// <summary>A desktop, or a laptop with the battery removed.</summary>
    NoBattery,

    /// <summary>Plugged in.</summary>
    PluggedIn,

    /// <summary>Running on the battery. Doc 3.4 wants a warning before switching to High.</summary>
    OnBattery,
}

/// <summary>Reading and switching the active power scheme.</summary>
public interface IPowerPlanService
{
    /// <summary>Every scheme on this PC. Ultimate Performance is often absent.</summary>
    Task<IReadOnlyList<PowerPlan>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The active scheme's id, or <c>null</c> if Windows did not say.</summary>
    Task<Guid?> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>Switches the active scheme.</summary>
    Task SetActiveAsync(Guid planId, CancellationToken cancellationToken);
}

/// <summary>Reads the mains/battery state. Behind an interface so tests can pretend to be a laptop.</summary>
public interface IBatteryStatus
{
    /// <summary>The current state. Never throws — returns <see cref="BatteryState.Unknown"/> instead.</summary>
    BatteryState Current { get; }
}
