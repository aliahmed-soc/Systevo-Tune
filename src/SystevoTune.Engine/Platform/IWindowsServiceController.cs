namespace SystevoTune.Engine.Platform;

/// <summary>What a Windows service is doing right now. Values match <c>sc query</c>'s STATE codes.</summary>
public enum ServiceState
{
    /// <summary>Could not tell.</summary>
    Unknown = 0,

    /// <summary>Not running.</summary>
    Stopped = 1,

    /// <summary>Coming up.</summary>
    StartPending = 2,

    /// <summary>Going down.</summary>
    StopPending = 3,

    /// <summary>Running.</summary>
    Running = 4,

    /// <summary>No such service on this PC.</summary>
    NotInstalled = 1060,
}

/// <summary>
/// Stopping and starting Windows services.
/// </summary>
/// <remarks>
/// Golden rule 4 forbids touching services. The human granted exactly one exception in session 2
/// (decision H1/H2): Windows Update and BITS may be stopped around the update-cache cleanup, and
/// nowhere else. <see cref="Cleanup.CleanupWhitelist"/> enforces that scope at load time, so the
/// exception cannot spread by editing a data file.
/// <para>
/// Nothing here force-kills. A service that will not stop cleanly means the cleanup is skipped.
/// </para>
/// </remarks>
public interface IWindowsServiceController
{
    /// <summary>What the service is doing. Never throws for a service that is not installed.</summary>
    Task<ServiceState> GetStateAsync(string serviceName, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the service to stop and waits for it. Returns <c>false</c> if it is still running when
    /// the timeout runs out — the caller must then leave whatever it wanted to do undone.
    /// </summary>
    Task<bool> TryStopAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Starts the service and waits for it. Returns <c>false</c> if it did not come up.</summary>
    Task<bool> TryStartAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken);
}
