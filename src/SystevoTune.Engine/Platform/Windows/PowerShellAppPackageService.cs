using System.Text.Json;

namespace SystevoTune.Engine.Platform.Windows;

/// <summary>
/// Store apps through PowerShell, which doc 02 allows for exactly this kind of work.
/// </summary>
/// <remarks>
/// Depends only on <see cref="IProcessRunner"/>, so the JSON parsing is unit tested without
/// running anything. Never used by unit tests to reach a real package manager.
/// <para>
/// Output is asked for as JSON rather than a formatted table, so nothing here depends on column
/// widths or the display language.
/// </para>
/// </remarks>
public sealed class PowerShellAppPackageService(IProcessRunner processes) : IAppPackageService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AppPackage>> ListAsync(CancellationToken cancellationToken)
    {
        var result = await Run(
            "Get-AppxPackage | Select-Object Name,PackageFullName,InstallLocation | ConvertTo-Json -Compress",
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0 ? Parse(result.StandardOutput) : [];
    }

    /// <inheritdoc />
    public async Task RemoveAsync(AppPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = await Run(
            $"Remove-AppxPackage -Package '{Escape(package.FullName)}'", cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Windows would not remove '{package.Name}': {result.AllOutput.Trim()}");
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryReinstallAsync(AppPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        // Re-register from whatever is left on disk. This is the only route that does not need
        // the Store, and it only works while the package files survive.
        var script =
            $"$p = Get-AppxPackage -AllUsers -Name '{Escape(package.Name)}' | Select-Object -First 1; "
            + "if ($p -and $p.InstallLocation) { "
            + "Add-AppxPackage -DisableDevelopmentMode -Register (Join-Path $p.InstallLocation 'AppXManifest.xml') }";

        await Run(script, cancellationToken).ConfigureAwait(false);

        // Trust the outcome, not the exit code: the only question that matters is whether the
        // package is installed again.
        var installed = await ListAsync(cancellationToken).ConfigureAwait(false);
        return installed.Any(candidate => string.Equals(candidate.Name, package.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads <c>ConvertTo-Json</c> output, which is an object for one result and an array for many.</summary>
    internal static IReadOnlyList<AppPackage> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray()
                    .Select(ReadOne)
                    .OfType<AppPackage>()
                    .ToList(),
                JsonValueKind.Object => ReadOne(root) is { } single ? [single] : [],
                _ => [],
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static AppPackage? ReadOne(JsonElement element)
    {
        var name = Text(element, "Name");
        var fullName = Text(element, "PackageFullName");

        return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullName)
            ? null
            : new AppPackage(name, fullName, Text(element, "InstallLocation"));
    }

    private static string Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private Task<ProcessResult> Run(string script, CancellationToken cancellationToken)
        => processes.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], cancellationToken);

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
