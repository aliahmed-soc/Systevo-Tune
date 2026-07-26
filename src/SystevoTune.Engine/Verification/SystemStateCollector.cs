using System.Globalization;
using SystevoTune.Engine.Bloatware;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Startup;
using SystevoTune.Engine.Tweaks.Registry;
using SystevoTune.Engine.Tweaks.Services;

namespace SystevoTune.Engine.Verification;

/// <summary>
/// Reads everything the engine is capable of changing.
/// </summary>
/// <remarks>
/// The value list comes from the whitelists themselves, so a tweak added later is snapshotted
/// automatically. A hand-written list would drift, and a snapshot that misses the value a new
/// tweak changes is worse than no snapshot at all — it would report a clean diff over a real
/// difference.
/// <para>Read-only. Nothing here writes.</para>
/// </remarks>
public sealed class SystemStateCollector(
    IRegistryService registry,
    IPowerPlanService powerPlans,
    StartupManager startup,
    RegistryTweakCatalog registryTweaks,
    ServiceWhitelist serviceWhitelist,
    BloatwareWhitelist bloatware,
    IWindowsServiceController? services = null,
    IAppPackageService? packages = null,
    TimeProvider? timeProvider = null)
{
    /// <summary>Services always snapshotted, whatever the whitelist holds.</summary>
    /// <remarks>
    /// The update-cache cleanup stops these (decision H1), so if it left one down the diff has to
    /// notice. They are read, never written, outside that one path.
    /// </remarks>
    private static readonly string[] AlwaysWatched = ["wuauserv", "bits"];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Takes a snapshot. Anything unreadable is recorded as unreadable, not skipped.</summary>
    public async Task<SystemStateSnapshot> CaptureAsync(string label, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        return new SystemStateSnapshot
        {
            Label = label,
            TakenAt = _time.GetLocalNow().DateTime,
            PowerSchemes = await CapturePowerSchemesAsync(cancellationToken).ConfigureAwait(false),
            Registry = CaptureRegistry(cancellationToken),
            Services = await CaptureServicesAsync(cancellationToken).ConfigureAwait(false),
            StartupItems = CaptureStartup(cancellationToken),
            Packages = await CapturePackagesAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<IReadOnlyList<PowerSchemeState>> CapturePowerSchemesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var schemes = await powerPlans.ListAsync(cancellationToken).ConfigureAwait(false);
            var active = await powerPlans.GetActiveAsync(cancellationToken).ConfigureAwait(false);

            return schemes
                .Select(scheme => new PowerSchemeState(scheme.Id.ToString("D"), scheme.Name, scheme.Id == active))
                .OrderBy(scheme => scheme.Guid, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [new PowerSchemeState("unreadable", ex.Message, false)];
        }
    }

    private IReadOnlyDictionary<string, string?> CaptureRegistry(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in EveryWatchedValue())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = reference.ToString();
            try
            {
                values[key] = registry.GetValue(reference)?.ToLogValue();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                values[key] = "unreadable: " + ex.Message;
            }
        }

        return values;
    }

    /// <summary>Every registry value any whitelist names. Duplicates collapse in the dictionary.</summary>
    private IEnumerable<RegistryValueRef> EveryWatchedValue()
    {
        foreach (var value in registryTweaks.Tweaks.SelectMany(tweak => tweak.Values))
        {
            yield return value.ToRef();
        }

        foreach (var service in serviceWhitelist.Services)
        {
            yield return ServiceWhitelist.StartValueRef(service.Name);
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> CaptureServicesAsync(CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (services is null)
        {
            return states;
        }

        var names = serviceWhitelist.Services
            .Select(service => service.Name)
            .Concat(AlwaysWatched)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            try
            {
                states[name] = (await services.GetStateAsync(name, cancellationToken).ConfigureAwait(false))
                    .ToString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                states[name] = "unreadable: " + ex.Message;
            }
        }

        return states;
    }

    private IReadOnlyDictionary<string, string> CaptureStartup(CancellationToken cancellationToken)
    {
        try
        {
            return startup.List(cancellationToken)
                .ToDictionary(item => item.Id, item => item.State.ToString(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["unreadable"] = ex.Message,
            };
        }
    }

    private async Task<IReadOnlyList<string>> CapturePackagesAsync(CancellationToken cancellationToken)
    {
        if (packages is null)
        {
            return [];
        }

        try
        {
            var installed = await packages.ListAsync(cancellationToken).ConfigureAwait(false);
            var watched = bloatware.Packages.Select(package => package.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return installed
                .Where(package => watched.Contains(package.Name))
                .Select(package => package.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ["unreadable: " + ex.Message];
        }
    }

    /// <summary>How many things a snapshot is watching, for the report header.</summary>
    public static int CountWatched(SystemStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.PowerSchemes.Count
            + snapshot.Registry.Count
            + snapshot.Services.Count
            + snapshot.StartupItems.Count
            + snapshot.Packages.Count;
    }

    /// <summary>A short description of coverage, for the report header.</summary>
    public static string DescribeCoverage(SystemStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{snapshot.PowerSchemes.Count} power scheme(s), {snapshot.Registry.Count} registry value(s), "
            + $"{snapshot.Services.Count} service(s), {snapshot.StartupItems.Count} startup item(s), "
            + $"{snapshot.Packages.Count} watched package(s)");
    }
}
