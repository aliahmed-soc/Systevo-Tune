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
/// Registry paths and the PowerShell command are listed in the windows-verified-paths skill under
/// UNVERIFIED. They must be checked against Microsoft docs before any VM run.
/// </para>
/// </remarks>
public sealed class RestorePointService(IRegistryService registry, IProcessRunner processes) : IRestorePointService
{
    /// <summary>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore — UNVERIFIED.</summary>
    internal static readonly RegistryValueRef SessionInterval = new(
        RegistryRoot.LocalMachine,
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore",
        "RPSessionInterval");

    /// <summary>HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore — UNVERIFIED.</summary>
    internal static readonly RegistryValueRef DisabledByPolicy = new(
        RegistryRoot.LocalMachine,
        @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore",
        "DisableSR");

    /// <summary>Phrases Windows uses when it declines because it made a point recently.</summary>
    private static readonly string[] FrequencyLimitPhrases =
    [
        "already been created",
        "within the past",
    ];

    /// <summary>Phrases Windows uses when System Restore is switched off.</summary>
    private static readonly string[] DisabledPhrases =
    [
        "system restore is disabled",
        "system protection is turned off",
        "0x81000203",
    ];

    /// <inheritdoc />
    public bool IsSystemRestoreEnabled()
    {
        // Policy wins: DisableSR = 1 switches System Restore off for the whole machine.
        if (ReadDword(DisabledByPolicy) == 1)
        {
            return false;
        }

        // RPSessionInterval = 0 means restore point creation is switched off.
        // A missing value is the default state, which is enabled.
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

        var output = result.AllOutput;

        // Windows reports the frequency limit as a warning and still exits 0, so check the text
        // before trusting the exit code.
        if (Mentions(output, FrequencyLimitPhrases))
        {
            return new RestorePointResult(
                RestorePointStatus.Skipped,
                "Windows did not create a new restore point because it made one recently. "
                + "An earlier restore point should still be available.",
                output.Trim());
        }

        if (Mentions(output, DisabledPhrases))
        {
            return new RestorePointResult(
                RestorePointStatus.Disabled,
                "Windows reported that System Restore is switched off, so no restore point was created.",
                output.Trim());
        }

        if (result.ExitCode != 0)
        {
            return new RestorePointResult(
                RestorePointStatus.Failed,
                $"Windows could not create a restore point (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}).",
                output.Trim());
        }

        return new RestorePointResult(RestorePointStatus.Created, $"Restore point created: {description}");
    }

    /// <summary>
    /// <c>MODIFY_SETTINGS</c> is the restore point type for a settings change, which is what a
    /// tune-up run is.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(string description) =>
    [
        "-NoProfile",
        "-NonInteractive",
        "-Command",
        $"Checkpoint-Computer -Description '{description.Replace("'", "''", StringComparison.Ordinal)}' "
        + "-RestorePointType MODIFY_SETTINGS",
    ];

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
