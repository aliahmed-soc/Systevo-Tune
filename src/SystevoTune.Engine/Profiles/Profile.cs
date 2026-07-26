using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystevoTune.Engine.Profiles;

/// <summary>What kind of tweak a profile step builds.</summary>
public enum ProfileStepKind
{
    /// <summary>A cleanup group from the cleanup whitelist.</summary>
    Cleanup,

    /// <summary>The active power scheme.</summary>
    PowerPlan,

    /// <summary>A registry tweak from the registry whitelist.</summary>
    Registry,
}

/// <summary>One step in a profile.</summary>
public sealed record ProfileStep
{
    /// <summary>Which builder handles this step.</summary>
    [JsonPropertyName("kind")]
    public required ProfileStepKind Kind { get; init; }

    /// <summary>Whitelist id, for cleanup and registry steps.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// Power scheme ids in preference order. The first one this PC has wins, so Gaming can ask
    /// for Ultimate and fall back to High.
    /// </summary>
    [JsonPropertyName("preferred")]
    public IReadOnlyList<string>? Preferred { get; init; }
}

/// <summary>A preset: an ordered list of tweaks that run through the normal log and undo pipeline.</summary>
public sealed record Profile
{
    /// <summary>Stable id, e.g. <c>gaming</c>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>English name.</summary>
    [JsonPropertyName("nameEn")]
    public required string NameEn { get; init; }

    /// <summary>Arabic name.</summary>
    [JsonPropertyName("nameAr")]
    public required string NameAr { get; init; }

    /// <summary>English description.</summary>
    [JsonPropertyName("descriptionEn")]
    public string? DescriptionEn { get; init; }

    /// <summary>Arabic description.</summary>
    [JsonPropertyName("descriptionAr")]
    public string? DescriptionAr { get; init; }

    /// <summary>Steps, in the order they run.</summary>
    [JsonPropertyName("steps")]
    public required IReadOnlyList<ProfileStep> Steps { get; init; }
}

/// <summary>The presets shipped with the engine.</summary>
public sealed class ProfileCatalog
{
    private const string ResourcePrefix = "SystevoTune.Engine.Profiles.";

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private ProfileCatalog(IReadOnlyList<Profile> profiles) => Profiles = profiles;

    /// <summary>Every shipped profile.</summary>
    public IReadOnlyList<Profile> Profiles { get; }

    /// <summary>Loads every profile embedded in the engine assembly.</summary>
    public static ProfileCatalog Load()
    {
        var assembly = typeof(ProfileCatalog).GetTypeInfo().Assembly;
        var profiles = new List<Profile>();

        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            profiles.Add(Parse(reader.ReadToEnd()));
        }

        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("No profiles are embedded in the build.");
        }

        return new ProfileCatalog(profiles);
    }

    /// <summary>Reads one profile from JSON.</summary>
    public static Profile Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        Profile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<Profile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"A profile could not be read: {ex.Message}", ex);
        }

        if (profile is null || profile.Steps.Count == 0)
        {
            throw new InvalidOperationException("A profile has no steps.");
        }

        return profile;
    }

    /// <summary>The profile with that id, or <c>null</c>.</summary>
    public Profile? Find(string profileId)
        => Profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
}
