using System.Globalization;
using System.Text.RegularExpressions;

namespace SystevoTune.Engine.Platform.Windows;

/// <summary>
/// Service control through <c>sc.exe</c>.
/// </summary>
/// <remarks>
/// <c>sc query</c> prints a numeric STATE code next to its localised name, so the parser reads
/// the number and ignores the words — the same choice made for powercfg, and for the same reason
/// (doc 07.4 requires a non-English Windows to work).
/// <para>
/// <c>sc stop</c> returns immediately, so stopping means asking and then polling until the state
/// settles or the timeout expires. Nothing is ever force-killed.
/// </para>
/// </remarks>
public sealed partial class ScServiceController(IProcessRunner processes, TimeProvider? timeProvider = null)
    : IWindowsServiceController
{
    /// <summary>How often to re-check while waiting for a service to settle.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<ServiceState> GetStateAsync(string serviceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var result = await processes.RunAsync("sc.exe", ["query", serviceName], cancellationToken)
            .ConfigureAwait(false);

        // 1060 is ERROR_SERVICE_DOES_NOT_EXIST. Not an error for us — just an absent service.
        if (result.ExitCode == (int)ServiceState.NotInstalled)
        {
            return ServiceState.NotInstalled;
        }

        return result.ExitCode == 0 ? ParseState(result.StandardOutput) : ServiceState.Unknown;
    }

    /// <inheritdoc />
    public Task<bool> TryStopAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken)
        => ChangeStateAsync(serviceName, "stop", ServiceState.Stopped, timeout, cancellationToken);

    /// <inheritdoc />
    public Task<bool> TryStartAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken)
        => ChangeStateAsync(serviceName, "start", ServiceState.Running, timeout, cancellationToken);

    private async Task<bool> ChangeStateAsync(
        string serviceName,
        string verb,
        ServiceState wanted,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        // Already there is a success, not a no-op to be argued with.
        if (await GetStateAsync(serviceName, cancellationToken).ConfigureAwait(false) == wanted)
        {
            return true;
        }

        await processes.RunAsync("sc.exe", [verb, serviceName], cancellationToken).ConfigureAwait(false);

        var deadline = _time.GetUtcNow() + timeout;
        while (_time.GetUtcNow() < deadline)
        {
            await Task.Delay(PollInterval, _time, cancellationToken).ConfigureAwait(false);

            if (await GetStateAsync(serviceName, cancellationToken).ConfigureAwait(false) == wanted)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the STATE code out of <c>sc query</c> output. The number is the same in every
    /// language; the word beside it is not.
    /// </summary>
    internal static ServiceState ParseState(string output)
    {
        var match = StateLine().Match(output ?? string.Empty);
        if (!match.Success
            || !int.TryParse(match.Groups["code"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return ServiceState.Unknown;
        }

        return Enum.IsDefined((ServiceState)code) ? (ServiceState)code : ServiceState.Unknown;
    }

    [GeneratedRegex(@"STATE\s*:\s*(?<code>\d+)", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase)]
    private static partial Regex StateLine();
}
