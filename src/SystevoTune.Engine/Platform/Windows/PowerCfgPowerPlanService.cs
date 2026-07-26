using System.Text.RegularExpressions;

namespace SystevoTune.Engine.Platform.Windows;

/// <summary>
/// Power schemes via <c>powercfg</c>.
/// </summary>
/// <remarks>
/// Depends only on <see cref="IProcessRunner"/>, so its parsing is fully unit tested.
/// <para>
/// The parser deliberately never reads the labels. <c>powercfg /list</c> prints
/// "Power Scheme GUID:" and "(Balanced)" in the system language, so doc 07.4's non-English
/// Windows case would break any name-based match. It matches the GUID and the trailing
/// <c>*</c> instead, both of which are the same in every language.
/// </para>
/// </remarks>
public sealed partial class PowerCfgPowerPlanService(IProcessRunner processes) : IPowerPlanService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PowerPlan>> ListAsync(CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("powercfg.exe", ["/list"], cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"powercfg /list failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return Parse(result.StandardOutput);
    }

    /// <inheritdoc />
    public async Task<Guid?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var plans = await ListAsync(cancellationToken).ConfigureAwait(false);
        return plans.FirstOrDefault(plan => plan.IsActive)?.Id;
    }

    /// <inheritdoc />
    public async Task SetActiveAsync(Guid planId, CancellationToken cancellationToken)
    {
        var result = await processes
            .RunAsync("powercfg.exe", ["/setactive", planId.ToString("D")], cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"powercfg /setactive failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }
    }

    /// <summary>
    /// Pulls schemes out of <c>powercfg /list</c>. One per line holding a GUID; the name is
    /// whatever sits in brackets, and a trailing <c>*</c> marks the active one.
    /// </summary>
    internal static IReadOnlyList<PowerPlan> Parse(string output)
    {
        var plans = new List<PowerPlan>();

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = PlanLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            plans.Add(new PowerPlan(
                Guid.Parse(match.Groups["guid"].Value),
                match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : string.Empty,
                line.TrimEnd().EndsWith('*')));
        }

        return plans;
    }

    [GeneratedRegex(
        @"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(\s*\((?<name>[^)]*)\))?",
        RegexOptions.ExplicitCapture)]
    private static partial Regex PlanLine();
}
