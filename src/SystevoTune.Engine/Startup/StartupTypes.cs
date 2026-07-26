using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystevoTune.Engine.Platform;

namespace SystevoTune.Engine.Startup;

/// <summary>Where a startup item comes from.</summary>
public enum StartupKind
{
    /// <summary>A value under a Run key.</summary>
    RegistryRun,

    /// <summary>A shortcut in a Startup folder.</summary>
    StartupFolder,
}

/// <summary>Whether Windows will launch the item at sign-in.</summary>
public enum StartupState
{
    /// <summary>It runs.</summary>
    Enabled,

    /// <summary>It is switched off but still present. Doc 3.2: never deleted.</summary>
    Disabled,
}

/// <summary>One place startup items live, as written in the whitelist file.</summary>
public sealed record StartupLocation
{
    /// <summary>Stable id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>English name.</summary>
    [JsonPropertyName("nameEn")]
    public required string NameEn { get; init; }

    /// <summary>Arabic name.</summary>
    [JsonPropertyName("nameAr")]
    public required string NameAr { get; init; }

    /// <summary>Registry Run key or Startup folder.</summary>
    [JsonPropertyName("kind")]
    public required StartupKind Kind { get; init; }

    /// <summary>Root of the Run key. Only for <see cref="StartupKind.RegistryRun"/>.</summary>
    [JsonPropertyName("root")]
    public string? Root { get; init; }

    /// <summary>Path of the Run key. Only for <see cref="StartupKind.RegistryRun"/>.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>Tokenised folder path. Only for <see cref="StartupKind.StartupFolder"/>.</summary>
    [JsonPropertyName("folder")]
    public string? Folder { get; init; }

    /// <summary>Root of the StartupApproved key.</summary>
    [JsonPropertyName("approvedRoot")]
    public required string ApprovedRoot { get; init; }

    /// <summary>Path of the StartupApproved key — the only key the engine writes.</summary>
    [JsonPropertyName("approvedKey")]
    public required string ApprovedKey { get; init; }

    /// <summary>Where the item's enabled/disabled flag lives.</summary>
    public RegistryValueRef ApprovedRef(string itemName)
        => new(ParseRoot(ApprovedRoot), ApprovedKey, itemName);

    /// <summary>Where the Run values live.</summary>
    public (RegistryRoot Root, string Key) RunKey()
        => Kind is StartupKind.RegistryRun && Root is not null && Key is not null
            ? (ParseRoot(Root), Key)
            : throw new InvalidOperationException($"Startup location '{Id}' is not a Run key.");

    /// <summary>The real folder path for a Startup folder location.</summary>
    public string ResolveFolder(IEnvironmentPaths environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (Kind is not StartupKind.StartupFolder || Folder is null)
        {
            throw new InvalidOperationException($"Startup location '{Id}' is not a folder.");
        }

        var resolved = Folder
            .Replace("{APPDATA}", environment.AppData, StringComparison.Ordinal)
            .Replace("{PROGRAMDATA}", environment.ProgramData, StringComparison.Ordinal);

        if (resolved.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Startup location '{Id}' uses a token the engine does not know.");
        }

        return Path.TrimEndingDirectorySeparator(resolved);
    }

    internal static RegistryRoot ParseRoot(string root) => root switch
    {
        "HKLM" => RegistryRoot.LocalMachine,
        "HKCU" => RegistryRoot.CurrentUser,
        _ => throw new InvalidOperationException($"'{root}' is not a registry root the engine uses."),
    };
}

/// <summary>One app that starts with Windows.</summary>
/// <param name="Name">The Run value name, or the shortcut's file name.</param>
/// <param name="Command">What it launches. For a folder item, the shortcut's path.</param>
/// <param name="LocationId">Which whitelist location it came from.</param>
/// <param name="Kind">Run key or Startup folder.</param>
/// <param name="State">Whether it currently runs.</param>
public sealed record StartupItem(
    string Name,
    string Command,
    string LocationId,
    StartupKind Kind,
    StartupState State)
{
    /// <summary>Stable id for profiles and the UI, e.g. <c>run-current-user/OneDrive</c>.</summary>
    public string Id => $"{LocationId}/{Name}";
}

/// <summary>The startup locations, from <c>Whitelists/startup-locations.json</c>.</summary>
public sealed class StartupLocationCatalog
{
    private const string ResourceName = "SystevoTune.Engine.Whitelists.startup-locations.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private StartupLocationCatalog(IReadOnlyList<StartupLocation> locations) => Locations = locations;

    /// <summary>The locations, in file order.</summary>
    public IReadOnlyList<StartupLocation> Locations { get; }

    /// <summary>Loads the catalogue shipped inside the engine assembly.</summary>
    public static StartupLocationCatalog Load()
    {
        using var stream = typeof(StartupLocationCatalog).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The startup catalogue '{ResourceName}' is missing from the build.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    /// <summary>Loads a catalogue from JSON. Used by tests and by <see cref="Load"/>.</summary>
    public static StartupLocationCatalog Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        CatalogFile? file;
        try
        {
            file = JsonSerializer.Deserialize<CatalogFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The startup catalogue could not be read: {ex.Message}", ex);
        }

        if (file?.Locations is null || file.Locations.Count == 0)
        {
            throw new InvalidOperationException("The startup catalogue lists no locations.");
        }

        foreach (var location in file.Locations)
        {
            // Fail at load rather than mid-run if a root is misspelt or a kind lacks its fields.
            StartupLocation.ParseRoot(location.ApprovedRoot);

            if (location.Kind is StartupKind.RegistryRun)
            {
                location.RunKey();
            }
            else if (location.Folder is null)
            {
                throw new InvalidOperationException($"Startup location '{location.Id}' names no folder.");
            }
        }

        return new StartupLocationCatalog(file.Locations);
    }

    private sealed record CatalogFile
    {
        [JsonPropertyName("locations")]
        public IReadOnlyList<StartupLocation>? Locations { get; init; }
    }
}
