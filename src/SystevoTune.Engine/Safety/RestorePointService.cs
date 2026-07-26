using System.Globalization;
using SystevoTune.Engine.Platform;

namespace SystevoTune.Engine.Safety;

/// <summary>
/// Creates a System Restore point by calling <c>Checkpoint-Computer</c> through PowerShell
/// (doc 02 allows PowerShell for this kind of work).
/// </summary>
/// <remarks>
/// Depends only on <see cref="IRegistryService"/> and <see cref="IProcessRunner"/>, so all of its
/// decision-making is unit tested with fakes. Nothing here runs during a test.
/// <para>
/// Closes open questions O3 and O5. The outcome is decided by <b>counting restore points before
/// and after</b> using the documented <c>Get-ComputerRestorePoint</c> cmdlet, not by matching
/// Windows' English prose. Counting works identically on an Arabic Windows, which doc 07.4
/// requires. The old phrase match survives only as a fallback for the message.
/// </para>
/// </remarks>
public sealed class RestorePointService(IRegistryService registry, IProcessRunner processes) : IRestorePointService
{
    /// <summary>Marker the script emits so we parse our own output, never Windows'.</summary>
    internal const string ResultMarker = "SYSTEVO_RP;";

    /// <summary>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore — undocumented (N8).</summary>
    internal static readonly RegistryValueRef SessionInterval = new(
        RegistryRoot.LocalMachine,
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore",
        "RPSessionInterval");

    /// <summary>HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore — undocumented (N9).</summary>
    internal static readonly RegistryValueRef DisabledByPolicy = new(
        RegistryRoot.LocalMachine,
        @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore",
        "DisableSR");

    /// <summary>Phrases Windows uses when System Restore is switched off. English only, best effort.</summary>
    private static readonly string[] DisabledPhrases =
    [
        "system restore is disabled",
        "system protection is turned off",
        "0x81000203",
    ];

    /// <inheritdoc />
    /// <remarks>
    /// A hint, not an authority. Both values it reads are undocumented (N8, N9), so a wrong answer
    /// here must never be the only thing standing between the user and a restore point. The real
    /// outcome comes from <see cref="CreateAsync"/> counting points.
    /// </remarks>
    public bool IsSystemRestoreEnabled()
    {
        if (ReadDword(DisabledByPolicy) == 1)
        {
            return false;
        }

        return ReadDword(SessionInterval) != 0;
    }

    /// <inheritdoc />
    /// <remarks>Cancellation propagates as <see cref="OperationCanceledException"/>; nothing else does.</remarks>
    public async Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (!IsSystemRestoreEnabled())
        {
            return new RestorePointResult(
                RestorePointStatus.Disabled,
                "System Restore is switched off on this PC, so no restore point was created. "
                + "Turn it on in System Protection, or continue knowing that only the app's own Undo can roll changes back.");
        }

        ProcessResult result;
        try
        {
            result = await processes.RunAsync("powershell.exe", BuildArguments(description), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RestorePointResult(
                RestorePointStatus.Failed,
                "Could not ask Windows for a restore point.",
                ex.Message);
        }

        return Interpret(description, result.AllOutput, result.ExitCode);
    }

    /// <summary>
    /// Works out what happened from the before/after counts. Language-independent by design.
    /// </summary>
    internal static RestorePointResult Interpret(string description, string output, int exitCode)
    {
        var counts = ParseCounts(output);

        if (counts is var (before, after))
        {
            if (after > before)
            {
                return new RestorePointResult(RestorePointStatus.Created, $"Restore point created: {description}");
            }

            // Nothing new, but points exist — this is Windows' once-a-day limit. The user still
            // has something to roll back to, so it is a warning rather than a failure.
            if (before > 0)
            {
                return new RestorePointResult(
                    RestorePointStatus.Skipped,
                    $"Windows did not create a new restore point because it made one recently. "
                    + $"{before.ToString(CultureInfo.InvariantCulture)} restore point(s) already exist, "
                    + "so there is still something to roll back to.",
                    output.Trim());
            }

            // No points before, none after: nothing to fall back on.
            return new RestorePointResult(
                RestorePointStatus.Failed,
                "Windows did not create a restore point, and this PC has none from earlier.",
                output.Trim());
        }

        // The script did not report counts at all, so Get-ComputerRestorePoint itself failed.
        // That usually means System Restore is off despite what the registry hinted.
        if (Mentions(output, DisabledPhrases))
        {
            return new RestorePointResult(
                RestorePointStatus.Disabled,
                "Windows reported that System Restore is switched off, so no restore point was created.",
                output.Trim());
        }

        return new RestorePointResult(
            RestorePointStatus.Failed,
            $"Windows could not create a restore point (exit code {exitCode.ToString(CultureInfo.InvariantCulture)}).",
            output.Trim());
    }

    /// <summary>Reads the <c>SYSTEVO_RP;before=N;after=M</c> line the script emits.</summary>
    internal static (int Before, int After)? ParseCounts(string output)
    {
        if (output is null)
        {
            return null;
        }

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var marker = line.IndexOf(ResultMarker, StringComparison.Ordinal);
            if (marker < 0)
            {
                continue;
            }

            var parts = line[(marker + ResultMarker.Length)..].Split(';', StringSplitOptions.RemoveEmptyEntries);
            var before = FindCount(parts, "before=");
            var after = FindCount(parts, "after=");

            if (before is not null && after is not null)
            {
                return (before.Value, after.Value);
            }
        }

        return null;
    }

    private static int? FindCount(string[] parts, string prefix)
    {
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(part.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Counts restore points, tries to create one, counts again, and reports both numbers.
    /// </summary>
    /// <remarks>
    /// <c>MODIFY_SETTINGS</c> is the restore point type for a settings change, which is what a
    /// tune-up run is. <c>Get-ComputerRestorePoint</c> and <c>Checkpoint-Computer</c> are both
    /// documented Windows PowerShell 5.1 cmdlets — do not switch this to <c>pwsh.exe</c>
    /// (decision 29).
    /// </remarks>
    internal static IReadOnlyList<string> BuildArguments(string description)
    {
        var safe = description.Replace("'", "''", StringComparison.Ordinal);

        // SilentlyContinue so a failed Checkpoint-Computer still lets the second count run:
        // the counts are what decide the outcome, not the error.
        var script =
            "$ErrorActionPreference='SilentlyContinue'; "
            + "$before=@(Get-ComputerRestorePoint).Count; "
            + $"Checkpoint-Computer -Description '{safe}' -RestorePointType MODIFY_SETTINGS; "
            + "$after=@(Get-ComputerRestorePoint).Count; "
            + $"Write-Output ('{ResultMarker}before=' + $before + ';after=' + $after)";

        return ["-NoProfile", "-NonInteractive", "-Command", script];
    }

    private int? ReadDword(RegistryValueRef reference)
    {
        var value = registry.GetValue(reference);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool Mentions(string output, string[] phrases)
        => phrases.Any(phrase => output.Contains(phrase, StringComparison.OrdinalIgnoreCase));
}
