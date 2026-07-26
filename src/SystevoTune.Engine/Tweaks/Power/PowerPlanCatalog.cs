using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystevoTune.Engine.Tweaks.Power;

/// <summary>One power scheme from the whitelist file.</summary>
public sealed record PowerPlanEntry
{
    /// <summary>Stable id used in profiles, e.g. <c>high-performance</c>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Windows' GUID for the scheme.</summary>
    [JsonPropertyName("guid")]
    public required Guid Guid { get; init; }

    /// <summary>English name.</summary>
    [JsonPropertyName("nameEn")]
    public required string NameEn { get; init; }

    /// <summary>Arabic name.</summary>
    [JsonPropertyName("nameAr")]
    public required string NameAr { get; init; }
}

/// <summary>
/// The power scheme GUIDs, from <c>Whitelists/power-plans.json</c>. No GUID appears in C#.
/// </summary>
public sealed class PowerPlanCatalog
{
    private const string ResourceName = "SystevoTune.Engine.Whitelists.power-plans.json";

    private PowerPlanCatalog(IReadOnlyList<PowerPlanEntry> plans) => Plans = plans;

    /// <summary>The schemes, in file order.</summary>
    public IReadOnlyList<PowerPlanEntry> Plans { get; }

    /// <summary>Loads the catalogue shipped inside the engine assembly.</summary>
    public static PowerPlanCatalog Load()
    {
        using var stream = typeof(PowerPlanCatalog).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The power plan catalogue '{ResourceName}' is missing from the build.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    /// <summary>Loads a catalogue from JSON. Used by tests and by <see cref="Load"/>.</summary>
    public static PowerPlanCatalog Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        CatalogFile? file;
        try
        {
            file = JsonSerializer.Deserialize<CatalogFile>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The power plan catalogue could not be read: {ex.Message}", ex);
        }

        if (file?.Plans is null || file.Plans.Count == 0)
        {
            throw new InvalidOperationException("The power plan catalogue lists no plans.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in file.Plans)
        {
            if (!seen.Add(plan.Id))
            {
                throw new InvalidOperationException($"The power plan catalogue lists '{plan.Id}' twice.");
            }
        }

        return new PowerPlanCatalog(file.Plans);
    }

    /// <summary>The scheme with that id, or <c>null</c>.</summary>
    public PowerPlanEntry? Find(string planId)
        => Plans.FirstOrDefault(plan => string.Equals(plan.Id, planId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The scheme with that GUID, or <c>null</c>. Used to name a plan in the log.</summary>
    public PowerPlanEntry? Find(Guid guid) => Plans.FirstOrDefault(plan => plan.Guid == guid);

    private sealed record CatalogFile
    {
        [JsonPropertyName("plans")]
        public IReadOnlyList<PowerPlanEntry>? Plans { get; init; }
    }
}
