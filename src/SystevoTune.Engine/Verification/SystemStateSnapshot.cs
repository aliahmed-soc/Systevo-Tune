using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystevoTune.Engine.Verification;

/// <summary>One power scheme as it stood when the snapshot was taken.</summary>
public sealed record PowerSchemeState(
    [property: JsonPropertyName("guid")] string Guid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("active")] bool IsActive);

/// <summary>
/// Everything the engine can touch, read at one moment.
/// </summary>
/// <remarks>
/// This is doc 07.2's "compare system state to the snapshot" turned into something the machine
/// can do. Every field is a value the engine is capable of changing, so a difference between two
/// snapshots is either a change we made or a change we failed to undo.
/// </remarks>
public sealed record SystemStateSnapshot
{
    /// <summary>Label for the report, e.g. <c>before</c>.</summary>
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    /// <summary>When it was taken.</summary>
    [JsonPropertyName("takenAt")]
    public required DateTime TakenAt { get; init; }

    /// <summary>Every power scheme on the PC, and which is active.</summary>
    [JsonPropertyName("powerSchemes")]
    public required IReadOnlyList<PowerSchemeState> PowerSchemes { get; init; }

    /// <summary>Every whitelisted registry value, as <c>HKLM\path::Name</c> to log value or <c>null</c>.</summary>
    [JsonPropertyName("registry")]
    public required IReadOnlyDictionary<string, string?> Registry { get; init; }

    /// <summary>Whitelisted services, name to state.</summary>
    [JsonPropertyName("services")]
    public required IReadOnlyDictionary<string, string> Services { get; init; }

    /// <summary>Startup items, id to enabled/disabled.</summary>
    [JsonPropertyName("startupItems")]
    public required IReadOnlyDictionary<string, string> StartupItems { get; init; }

    /// <summary>Installed Store packages the bloatware whitelist knows about.</summary>
    [JsonPropertyName("packages")]
    public required IReadOnlyList<string> Packages { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The snapshot as JSON, for the human to keep or diff by hand.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);
}

/// <summary>One thing that is not the way it was.</summary>
/// <param name="Area">Which part of the system — <c>PowerPlan</c>, <c>Registry</c>, and so on.</param>
/// <param name="Target">What differs.</param>
/// <param name="Before">Value in the first snapshot.</param>
/// <param name="After">Value in the last snapshot.</param>
public sealed record StateDifference(string Area, string Target, string? Before, string? After)
{
    /// <summary>One line for the report.</summary>
    public override string ToString() => $"{Area}  {Target}\n    was: {Show(Before)}\n    now: {Show(After)}";

    private static string Show(string? value) => value ?? "(not set)";
}

/// <summary>
/// Compares two snapshots. Pure, so the diff logic is unit tested rather than trusted.
/// </summary>
public static class StateDiff
{
    /// <summary>
    /// Everything that differs between two snapshots. An empty list is what doc 07.2 calls a pass.
    /// </summary>
    public static IReadOnlyList<StateDifference> Compare(SystemStateSnapshot before, SystemStateSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var differences = new List<StateDifference>();

        ComparePowerSchemes(before, after, differences);
        CompareMaps("Registry", before.Registry, after.Registry, differences);
        CompareMaps("Service", Widen(before.Services), Widen(after.Services), differences);
        CompareMaps("Startup", Widen(before.StartupItems), Widen(after.StartupItems), differences);
        ComparePackages(before, after, differences);

        return differences;
    }

    private static void ComparePowerSchemes(
        SystemStateSnapshot before,
        SystemStateSnapshot after,
        List<StateDifference> differences)
    {
        var beforeActive = before.PowerSchemes.FirstOrDefault(scheme => scheme.IsActive)?.Guid;
        var afterActive = after.PowerSchemes.FirstOrDefault(scheme => scheme.IsActive)?.Guid;

        if (!string.Equals(beforeActive, afterActive, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add(new StateDifference("PowerPlan", "active scheme", beforeActive, afterActive));
        }

        // A scheme the engine created and failed to delete is a difference too — doc 07.2 counts
        // anything left behind, not only settings that changed.
        var beforeIds = before.PowerSchemes.Select(scheme => scheme.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterIds = after.PowerSchemes.Select(scheme => scheme.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var added in afterIds.Except(beforeIds, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            differences.Add(new StateDifference("PowerPlan", "scheme left behind", null, added));
        }

        foreach (var removed in beforeIds.Except(afterIds, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            differences.Add(new StateDifference("PowerPlan", "scheme missing", removed, null));
        }
    }

    private static void ComparePackages(
        SystemStateSnapshot before,
        SystemStateSnapshot after,
        List<StateDifference> differences)
    {
        var beforeSet = before.Packages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterSet = after.Packages.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var missing in beforeSet.Except(afterSet, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            differences.Add(new StateDifference("Package", missing, "installed", "removed"));
        }

        foreach (var added in afterSet.Except(beforeSet, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            differences.Add(new StateDifference("Package", added, "removed", "installed"));
        }
    }

    private static void CompareMaps(
        string area,
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyDictionary<string, string?> after,
        List<StateDifference> differences)
    {
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            before.TryGetValue(key, out var was);
            after.TryGetValue(key, out var now);

            if (!string.Equals(was, now, StringComparison.Ordinal))
            {
                differences.Add(new StateDifference(area, key, was, now));
            }
        }
    }

    private static IReadOnlyDictionary<string, string?> Widen(IReadOnlyDictionary<string, string> map)
        => map.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);
}
