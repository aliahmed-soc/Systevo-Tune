using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystevoTune.Engine.Platform;

namespace SystevoTune.Engine.Cleanup;

/// <summary>One group of paths the user can tick, as written in the whitelist file.</summary>
public sealed record CleanupGroup
{
    /// <summary>Stable id used in profiles and log records.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>English name.</summary>
    [JsonPropertyName("nameEn")]
    public required string NameEn { get; init; }

    /// <summary>Arabic name.</summary>
    [JsonPropertyName("nameAr")]
    public required string NameAr { get; init; }

    /// <summary>Whether to walk subfolders.</summary>
    [JsonPropertyName("recursive")]
    public bool Recursive { get; init; } = true;

    /// <summary>Tokenised paths, resolved by <see cref="CleanupWhitelist.Resolve"/>.</summary>
    [JsonPropertyName("paths")]
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>
    /// Services to stop before deleting and start again afterwards. Almost always empty:
    /// <see cref="CleanupWhitelist"/> refuses any group but the update cache, and any service but
    /// Windows Update and BITS. See decisions H1 and H2.
    /// </summary>
    [JsonPropertyName("stopServices")]
    public IReadOnlyList<string> StopServices { get; init; } = [];
}

/// <summary>
/// The cleanup whitelist. Doc 5.4: paths come from this file only, never from code.
/// </summary>
/// <remarks>
/// Shipped as an embedded resource so a user cannot point cleanup somewhere else by editing a
/// file next to the exe. Editing the repo copy and rebuilding is the only way to change it.
/// </remarks>
public sealed class CleanupWhitelist
{
    private const string ResourceName = "SystevoTune.Engine.Whitelists.cleanup-paths.json";

    /// <summary>Folder names that are always off limits, whatever the whitelist says.</summary>
    private static readonly string[] ForbiddenProfileFolders =
        ["Documents", "Desktop", "Downloads", "Pictures", "Videos", "Music"];

    /// <summary>
    /// The single group allowed to stop services, and the only services it may stop.
    /// </summary>
    /// <remarks>
    /// Golden rule 4 forbids touching services. Decision H1 carved out one exception for the
    /// Windows Update cache; decision H2 says it is the only one. Hard-coding the pair here means
    /// the exception cannot be widened by editing the whitelist file — someone adding
    /// <c>"stopServices": ["WinDefend"]</c> gets a load-time refusal, not a disabled Defender.
    /// </remarks>
    private const string ServiceStopGroupId = "windows-update-cache";

    private static readonly string[] ServiceStopAllowed = ["wuauserv", "bits"];

    private CleanupWhitelist(IReadOnlyList<CleanupGroup> groups) => Groups = groups;

    /// <summary>The groups, in file order.</summary>
    public IReadOnlyList<CleanupGroup> Groups { get; }

    /// <summary>Loads the whitelist shipped inside the engine assembly.</summary>
    public static CleanupWhitelist Load()
    {
        using var stream = typeof(CleanupWhitelist).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The cleanup whitelist '{ResourceName}' is missing from the build.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    /// <summary>Loads a whitelist from JSON. Used by tests and by <see cref="Load"/>.</summary>
    /// <exception cref="InvalidOperationException">The file is malformed or names a duplicate group.</exception>
    public static CleanupWhitelist Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        WhitelistFile? file;
        try
        {
            file = JsonSerializer.Deserialize<WhitelistFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The cleanup whitelist could not be read: {ex.Message}", ex);
        }

        if (file?.Groups is null || file.Groups.Count == 0)
        {
            throw new InvalidOperationException("The cleanup whitelist has no groups.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in file.Groups)
        {
            if (!seen.Add(group.Id))
            {
                throw new InvalidOperationException($"The cleanup whitelist lists group '{group.Id}' twice.");
            }

            if (group.Paths.Count == 0)
            {
                throw new InvalidOperationException($"Cleanup group '{group.Id}' lists no paths.");
            }

            GuardServiceStops(group);
        }

        return new CleanupWhitelist(file.Groups);
    }

    /// <summary>The group with that id, or <c>null</c>.</summary>
    public CleanupGroup? Find(string groupId)
        => Groups.FirstOrDefault(group => string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Turns a whitelist path into a real one and refuses anything off limits.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The path uses an unknown token, is relative, or lands somewhere cleanup may never touch.
    /// </exception>
    public static string Resolve(string whitelistPath, IEnvironmentPaths environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(whitelistPath);
        ArgumentNullException.ThrowIfNull(environment);

        var resolved = whitelistPath
            .Replace("{USER_TEMP}", environment.UserTemp, StringComparison.Ordinal)
            .Replace("{WINDIR}", environment.WindowsDirectory, StringComparison.Ordinal)
            .Replace("{SYSTEM_DRIVE}", TrimSeparator(environment.SystemDrive), StringComparison.Ordinal);

        if (resolved.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{whitelistPath}' uses a token the engine does not know.");
        }

        if (!Path.IsPathFullyQualified(resolved))
        {
            throw new InvalidOperationException($"'{whitelistPath}' is not an absolute path.");
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved));
        GuardForbidden(full, environment);
        return full;
    }

    /// <summary>
    /// Keeps the one service-stopping exception exactly where the human put it.
    /// </summary>
    private static void GuardServiceStops(CleanupGroup group)
    {
        if (group.StopServices.Count == 0)
        {
            return;
        }

        if (!string.Equals(group.Id, ServiceStopGroupId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cleanup group '{group.Id}' asks to stop services. Only '{ServiceStopGroupId}' may do that.");
        }

        foreach (var service in group.StopServices)
        {
            if (!ServiceStopAllowed.Contains(service, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Cleanup may never stop '{service}'. Only {string.Join(" and ", ServiceStopAllowed)} are allowed, "
                    + "and only for the Windows Update cache.");
            }
        }
    }

    /// <summary>
    /// The last line of defence. Even if the whitelist file is edited badly, these never go.
    /// </summary>
    private static void GuardForbidden(string fullPath, IEnvironmentPaths environment)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath) ?? string.Empty);
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cleanup may not target a whole drive ('{fullPath}').");
        }

        var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.UserProfile));
        if (string.Equals(fullPath, profile, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cleanup may not target the user profile ('{fullPath}').");
        }

        if (string.Equals(fullPath, Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.WindowsDirectory)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cleanup may not target the Windows folder itself ('{fullPath}').");
        }

        foreach (var folder in ForbiddenProfileFolders)
        {
            var forbidden = Path.Combine(profile, folder);
            if (IsAtOrUnder(fullPath, forbidden))
            {
                throw new InvalidOperationException($"Cleanup may never touch '{folder}' ('{fullPath}').");
            }
        }
    }

    /// <summary>Whether <paramref name="candidate"/> is the folder or sits inside it.</summary>
    private static bool IsAtOrUnder(string candidate, string folder)
    {
        if (string.Equals(candidate, folder, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // The separator matters: C:\Users\a\Documents must not match C:\Users\a\DocumentsOld.
        return candidate.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimSeparator(string path) => Path.TrimEndingDirectorySeparator(path);

    private static readonly JsonSerializerOptions Options = new() { ReadCommentHandling = JsonCommentHandling.Skip };

    private sealed record WhitelistFile
    {
        [JsonPropertyName("groups")]
        public IReadOnlyList<CleanupGroup>? Groups { get; init; }
    }
}
