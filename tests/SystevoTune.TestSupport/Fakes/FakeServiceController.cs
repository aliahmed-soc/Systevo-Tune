using SystevoTune.Engine.Platform;

namespace SystevoTune.TestSupport;

/// <summary>
/// In-memory services. No unit test stops or starts a real one.
/// </summary>
public sealed class FakeServiceController : IWindowsServiceController
{
    private readonly Dictionary<string, ServiceState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Services that refuse to stop, standing in for one that hangs.</summary>
    public HashSet<string> RefusesToStop { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Services that refuse to start again after being stopped.</summary>
    public HashSet<string> RefusesToStart { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every stop and start, as <c>stop:name</c> / <c>start:name</c>, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Adds a service in the given state.</summary>
    public FakeServiceController With(string name, ServiceState state = ServiceState.Running)
    {
        _states[name] = state;
        return this;
    }

    /// <summary>The state a service is in now.</summary>
    public ServiceState StateOf(string name) => _states.GetValueOrDefault(name, ServiceState.NotInstalled);

    public Task<ServiceState> GetStateAsync(string serviceName, CancellationToken cancellationToken)
        => Task.FromResult(StateOf(serviceName));

    public Task<bool> TryStopAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add("stop:" + serviceName);

        if (RefusesToStop.Contains(serviceName))
        {
            return Task.FromResult(false);
        }

        _states[serviceName] = ServiceState.Stopped;
        return Task.FromResult(true);
    }

    public Task<bool> TryStartAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add("start:" + serviceName);

        if (RefusesToStart.Contains(serviceName))
        {
            return Task.FromResult(false);
        }

        _states[serviceName] = ServiceState.Running;
        return Task.FromResult(true);
    }
}
