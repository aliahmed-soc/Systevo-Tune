using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystevoTune.Engine.Bloatware;

/// <summary>One package the whitelist allows removing.</summary>
public sealed record BloatwareEntry
{
    /// <summary>Package short name, e.g. <c>Microsoft.BingNews</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>English name for the user.</summary>
    [JsonPropertyName("nameEn")]
    public required string NameEn { get; init; }

    /// <summary>Arabic name for the user.</summary>
    [JsonPropertyName("nameAr")]
    public required string NameAr { get; init; }

    /// <summary>
    /// Whether the human has approved removing this. Ships <c>false</c> on every entry — the
    /// module builds no tweak until it is flipped.
    /// </summary>
    [JsonPropertyName("approved")]
    public bool Approved { get; init; }

    /// <summary>One line explaining what is lost, shown before the user ticks it.</summary>
    [JsonPropertyName("whyEn")]
    public string? WhyEn { get; init; }
}

/// <summary>
/// The bloatware whitelist (doc 3.8). Nothing is removed that is not named here <b>and</b>
/// approved.
/// </summary>
/// <remarks>
/// Two gates on purpose. The list is the "what could be removed" question; <c>approved</c> is the
/// "has a human actually agreed" question. A conservative starter list that nobody signed off is
/// still a list of apps we would delete from someone's PC.
/// </remarks>
public sealed class BloatwareWhitelist
{
    private const string ResourceName = "SystevoTune.Engine.Whitelists.bloatware.json";

    /// <summary>
    /// Package name fragments that may never be removed, whatever the file says.
    /// </summary>
    /// <remarks>
    /// Removing any of these breaks Windows in ways Undo cannot fix: the Store itself is how a
    /// user would reinstall anything, <c>SecHealthUI</c> is the Windows Security window, and the
    /// framework packages are dependencies half the other apps are built on.
    /// </remarks>
    private static readonly string[] ForbiddenFragments =
    [
        "WindowsStore",
        "SecHealthUI",
        "Windows.Defender",
        "VCLibs",
        "NET.Native",
        "UI.Xaml",
        "DesktopAppInstaller",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "Client.CBS",
        "Client.Core",
        "Windows.Search",
        "AccountsControl",
        "CredDialogHost",
        "LockApp",
        "Windows.CloudExperienceHost",
        "Windows.ShellComponents",
        "XamlHost",
    ];

    private BloatwareWhitelist(IReadOnlyList<BloatwareEntry> packages) => Packages = packages;

    /// <summary>Every listed package, approved or not.</summary>
    public IReadOnlyList<BloatwareEntry> Packages { get; }

    /// <summary>Only the packages a human has approved. This is what the module acts on.</summary>
    public IReadOnlyList<BloatwareEntry> Approved => Packages.Where(package => package.Approved).ToList();

    /// <summary>Loads the whitelist shipped inside the engine assembly.</summary>
    public static BloatwareWhitelist Load()
    {
        using var stream = typeof(BloatwareWhitelist).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The bloatware whitelist '{ResourceName}' is missing from the build.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    /// <summary>Loads a whitelist from JSON. Used by tests and by <see cref="Load"/>.</summary>
    public static BloatwareWhitelist Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        WhitelistFile? file;
        try
        {
            file = JsonSerializer.Deserialize<WhitelistFile>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The bloatware whitelist could not be read: {ex.Message}", ex);
        }

        var packages = file?.Packages ?? [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            if (!seen.Add(package.Name))
            {
                throw new InvalidOperationException($"The bloatware whitelist lists '{package.Name}' twice.");
            }

            Guard(package.Name);
        }

        return new BloatwareWhitelist(packages);
    }

    /// <summary>Whether a package name is one the engine may never touch.</summary>
    public static bool IsForbidden(string packageName)
        => ForbiddenFragments.Any(fragment => packageName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static void Guard(string packageName)
    {
        if (IsForbidden(packageName))
        {
            throw new InvalidOperationException(
                $"'{packageName}' is part of Windows itself. Removing it would break the PC in a way Undo cannot fix.");
        }
    }

    private sealed record WhitelistFile
    {
        [JsonPropertyName("packages")]
        public IReadOnlyList<BloatwareEntry>? Packages { get; init; }
    }
}
