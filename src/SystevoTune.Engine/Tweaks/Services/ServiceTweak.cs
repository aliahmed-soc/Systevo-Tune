using System.Globalization;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.Engine.Tweaks.Services;

/// <summary>
/// Retunes one whitelisted service's start type (doc 3.3).
/// </summary>
/// <remarks>
/// Writes only the <c>Start</c> value under the service's key, which takes effect at the next
/// boot. It never stops a running service, so a tune-up cannot pull the rug out from under
/// something the user is in the middle of.
/// <para>
/// Because it is a registry write, its records carry the <c>Registry</c> module and are put back
/// by the already-tested <see cref="RegistryUndoHandler"/>. Doc 7.3 wants undo to restore the
/// exact previous start type rather than a default, which comes free from that.
/// </para>
/// </remarks>
public sealed class ServiceTweak(IRegistryService registry, ServiceEntry entry) : ITweak
{
    /// <inheritdoc />
    public string Id { get; } = "service." + entry.Name;

    /// <inheritdoc />
    public string Name { get; } = entry.NameEn;

    /// <summary>Registry, so the tested registry undo handler puts it back.</summary>
    public string Module => RegistryTweak.ModuleName;

    /// <inheritdoc />
    public Task<TweakPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var reference = ServiceWhitelist.StartValueRef(entry.Name);
        var current = registry.GetValue(reference);

        // No Start value means no such service on this PC. Creating one would invent a service
        // configuration rather than change one.
        if (current is null)
        {
            return Task.FromResult(TweakPlan.NotApplicable(Id, Name,
                $"{entry.NameEn} is not installed on this PC, so nothing was changed."));
        }

        var target = RegistryValue.Dword((int)entry.Start);
        if (current == target)
        {
            return Task.FromResult(TweakPlan.AlreadyApplied(Id, Name,
                $"{entry.NameEn} is already set to {entry.Start}."));
        }

        var change = new PlannedChange(
            Module,
            RegistryTweak.SetAction,
            reference.ToString(),
            current.ToLogValue(),
            target.ToLogValue(),
            $"Service {entry.NameEn}: {Describe(current)} to {entry.Start}. Takes effect after a restart.");

        return Task.FromResult(TweakPlan.Ready(Id, Name, [change], requiresRestart: true));
    }

    /// <inheritdoc />
    public Task ApplyChangeAsync(PlannedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var reference = RegistryValueRef.Parse(change.Target);
        var value = RegistryValue.FromLogValue(change.NewValue)
            ?? throw new InvalidOperationException($"'{change.Target}' has no new value to write.");

        registry.SetValue(reference, value);
        return Task.CompletedTask;
    }

    /// <summary>Names the start type where possible, so the log reads as words rather than digits.</summary>
    private static string Describe(RegistryValue value)
        => int.TryParse(value.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw)
           && Enum.IsDefined((ServiceStartType)raw)
            ? ((ServiceStartType)raw).ToString()
            : value.Data;
}

/// <summary>Builds tweaks for the whitelisted services.</summary>
public sealed class ServicesModule(ServiceWhitelist whitelist, IRegistryService registry)
{
    /// <summary>One tweak per whitelisted service, so the user can tick them individually.</summary>
    public IReadOnlyList<ITweak> CreateTweaks(IEnumerable<string>? serviceNames = null)
    {
        var wanted = serviceNames?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return whitelist.Services
            .Where(service => wanted is null || wanted.Contains(service.Name))
            .Select(ITweak (service) => new ServiceTweak(registry, service))
            .ToList();
    }
}
