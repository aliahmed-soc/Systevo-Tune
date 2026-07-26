using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystevoTune.Engine.Platform;

namespace SystevoTune.Engine.Tweaks.Services;

/// <summary>How Windows starts a service.</summary>
public enum ServiceStartType
{
    /// <summary>Boot driver. Not tunable.</summary>
    Boot = 0,

    /// <summary>System driver. Not tunable.</summary>
    System = 1,

    /// <summary>Starts with Windows.</summary>
    Automatic = 2,

    /// <summary>Starts only when something needs it.</summary>
    Manual = 3,

    /// <summary>Never starts.</summary>
    Disabled = 4,
}

/// <summary>One service the whitelist allows retuning.</summary>
public sealed record ServiceEntry
{
    /// <summary>The service's short name, e.g. <c>SysMain</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>English name for the user.</summary>
    [JsonPropertyName("nameEn")]
    public required string NameEn { get; init; }

    /// <summary>Arabic name for the user.</summary>
    [JsonPropertyName("nameAr")]
    public required string NameAr { get; init; }

    /// <summary>What to set the start type to.</summary>
    [JsonPropertyName("start")]
    public required ServiceStartType Start { get; init; }

    /// <summary>One line explaining why, shown before the user ticks it.</summary>
    [JsonPropertyName("whyEn")]
    public string? WhyEn { get; init; }
}

/// <summary>
/// The services whitelist. Ships empty on purpose — doc 3.3 wants a list of services *known* safe
/// to move to Manual, and that list has to be built by a human who verified each one.
/// </summary>
public sealed class ServiceWhitelist
{
    private const string ResourceName = "SystevoTune.Engine.Whitelists.services.json";

    /// <summary>The registry key holding every service's configuration.</summary>
    internal const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    /// <summary>The value that decides how a service starts.</summary>
    internal const string StartValueName = "Start";

    /// <summary>
    /// Never tunable, whatever the file says. Golden rule 4 and doc 5.6: security, network,
    /// audio, and printing stay exactly as Windows left them.
    /// </summary>
    private static readonly string[] ForbiddenServices =
    [
        // Security
        "WinDefend", "SecurityHealthService", "Sense", "WdNisSvc", "wscsvc", "MpsSvc", "BFE",
        // Network
        "Dhcp", "Dnscache", "NlaSvc", "netprofm", "WlanSvc", "LanmanWorkstation", "LanmanServer",
        "RpcSs", "RpcEptMapper", "nsi",
        // Audio
        "Audiosrv", "AudioEndpointBuilder",
        // Printing
        "Spooler",
        // Things that brick a login
        "ProfSvc", "UserManager", "Winlogon", "gpsvc", "CryptSvc", "TrustedInstaller", "EventLog",
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private ServiceWhitelist(IReadOnlyList<ServiceEntry> services) => Services = services;

    /// <summary>The services, in file order. Empty until a human fills the file in.</summary>
    public IReadOnlyList<ServiceEntry> Services { get; }

    /// <summary>Loads the whitelist shipped inside the engine assembly.</summary>
    public static ServiceWhitelist Load()
    {
        using var stream = typeof(ServiceWhitelist).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The services whitelist '{ResourceName}' is missing from the build.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    /// <summary>Loads a whitelist from JSON. Used by tests and by <see cref="Load"/>.</summary>
    public static ServiceWhitelist Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        WhitelistFile? file;
        try
        {
            file = JsonSerializer.Deserialize<WhitelistFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The services whitelist could not be read: {ex.Message}", ex);
        }

        // An empty list is the shipped state, not an error.
        var services = file?.Services ?? [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var service in services)
        {
            if (!seen.Add(service.Name))
            {
                throw new InvalidOperationException($"The services whitelist lists '{service.Name}' twice.");
            }

            Guard(service);
        }

        return new ServiceWhitelist(services);
    }

    /// <summary>Where a service's start type lives.</summary>
    public static RegistryValueRef StartValueRef(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (serviceName.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{serviceName}' is not a service name.");
        }

        return new RegistryValueRef(RegistryRoot.LocalMachine, $@"{ServicesKey}\{serviceName}", StartValueName);
    }

    /// <summary>
    /// The last line of defence. Runs whatever the file says, so a bad edit cannot disable
    /// Defender or the network stack.
    /// </summary>
    private static void Guard(ServiceEntry service)
    {
        if (ForbiddenServices.Contains(service.Name, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{service.Name}' is a security, network, audio, printing, or sign-in service. "
                + "The engine may never change it.");
        }

        if (service.Start is ServiceStartType.Boot or ServiceStartType.System)
        {
            throw new InvalidOperationException(
                $"'{service.Name}' would be set to {service.Start}, which is a driver start type the engine may not write.");
        }

        if (!Enum.IsDefined(service.Start))
        {
            throw new InvalidOperationException($"'{service.Name}' has an unknown start type.");
        }
    }

    private sealed record WhitelistFile
    {
        [JsonPropertyName("services")]
        public IReadOnlyList<ServiceEntry>? Services { get; init; }
    }
}
