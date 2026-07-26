using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystevoTune.Engine.Platform;

namespace SystevoTune.Engine.Tweaks.Registry;

/// <summary>One registry value a tweak writes, as written in the whitelist file.</summary>
public sealed record RegistryTweakValue
{
    /// <summary><c>HKLM</c> or <c>HKCU</c>.</summary>
    [JsonPropertyName("root")]
    public required string Root { get; init; }

    /// <summary>Key path under the root.</summary>
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    /// <summary>Value name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Value type.</summary>
    [JsonPropertyName("type")]
    public required RegistryValueType Type { get; init; }

    /// <summary>The data to write, as text.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>Where this value lives, in engine terms.</summary>
    public RegistryValueRef ToRef() => new(ParseRoot(Root), Key, Name);

    /// <summary>What this value should become.</summary>
    public RegistryValue ToValue() => new(Type, Data);

    private static RegistryRoot ParseRoot(string root) => root switch
    {
        "HKLM" => RegistryRoot.LocalMachine,
        "HKCU" => RegistryRoot.CurrentUser,
        _ => throw new InvalidOperationException($"'{root}' is not a registry root the engine uses."),
    };
}

/// <summary>One registry-backed tweak, as written in the whitelist file.</summary>
public sealed record RegistryTweakEntry
{
    /// <summary>Stable id used in profiles.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>English name.</summary>
    [JsonPropertyName("nameEn")]
    public required string NameEn { get; init; }

    /// <summary>Arabic name.</summary>
    [JsonPropertyName("nameAr")]
    public required string NameAr { get; init; }

    /// <summary>Whether the change only takes full effect after a restart.</summary>
    [JsonPropertyName("requiresRestart")]
    public bool RequiresRestart { get; init; }

    /// <summary>
    /// When true, the tweak is <see cref="TweakStatus.NotApplicable"/> unless the value already
    /// exists. Used for settings Windows only exposes on supported hardware, such as GPU scheduling.
    /// </summary>
    [JsonPropertyName("requiresExistingValue")]
    public bool RequiresExistingValue { get; init; }

    /// <summary>Free text for whoever verifies the paths. Not shown to users.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }

    /// <summary>The values this tweak writes.</summary>
    [JsonPropertyName("values")]
    public required IReadOnlyList<RegistryTweakValue> Values { get; init; }
}

/// <summary>
/// The registry tweaks, from <c>Whitelists/registry-tweaks.json</c>. Doc 5.4 and golden rule 5:
/// no registry path appears in C#.
/// </summary>
public sealed class RegistryTweakCatalog
{
    private const string ResourceName = "SystevoTune.Engine.Whitelists.registry-tweaks.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private RegistryTweakCatalog(IReadOnlyList<RegistryTweakEntry> tweaks) => Tweaks = tweaks;

    /// <summary>The tweaks, in file order.</summary>
    public IReadOnlyList<RegistryTweakEntry> Tweaks { get; }

    /// <summary>Loads the catalogue shipped inside the engine assembly.</summary>
    public static RegistryTweakCatalog Load()
    {
        using var stream = typeof(RegistryTweakCatalog).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The registry tweak catalogue '{ResourceName}' is missing from the build.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    /// <summary>Loads a catalogue from JSON. Used by tests and by <see cref="Load"/>.</summary>
    public static RegistryTweakCatalog Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        CatalogFile? file;
        try
        {
            file = JsonSerializer.Deserialize<CatalogFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The registry tweak catalogue could not be read: {ex.Message}", ex);
        }

        if (file?.Tweaks is null || file.Tweaks.Count == 0)
        {
            throw new InvalidOperationException("The registry tweak catalogue lists no tweaks.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tweak in file.Tweaks)
        {
            if (!seen.Add(tweak.Id))
            {
                throw new InvalidOperationException($"The registry tweak catalogue lists '{tweak.Id}' twice.");
            }

            if (tweak.Values.Count == 0)
            {
                throw new InvalidOperationException($"Registry tweak '{tweak.Id}' writes no values.");
            }

            // Fail at load rather than mid-apply if a root is misspelt.
            foreach (var value in tweak.Values)
            {
                value.ToRef();
            }
        }

        return new RegistryTweakCatalog(file.Tweaks);
    }

    /// <summary>The tweak with that id, or <c>null</c>.</summary>
    public RegistryTweakEntry? Find(string tweakId)
        => Tweaks.FirstOrDefault(tweak => string.Equals(tweak.Id, tweakId, StringComparison.OrdinalIgnoreCase));

    private sealed record CatalogFile
    {
        [JsonPropertyName("tweaks")]
        public IReadOnlyList<RegistryTweakEntry>? Tweaks { get; init; }
    }
}
