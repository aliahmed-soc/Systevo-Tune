using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace SystevoTune.App.Localization;

/// <summary>A language the app can run in.</summary>
/// <param name="Code">Two-letter code, matching the embedded <c>Strings/&lt;code&gt;.json</c>.</param>
/// <param name="NativeName">The language's name in its own language, for the switcher.</param>
/// <param name="IsRightToLeft">Whether the layout mirrors.</param>
public sealed record Language(string Code, string NativeName, bool IsRightToLeft)
{
    /// <summary>English.</summary>
    public static Language English { get; } = new("en", "English", IsRightToLeft: false);

    /// <summary>Arabic. Doc 08: a real edge, and RTL from the first commit rather than bolted on.</summary>
    public static Language Arabic { get; } = new("ar", "العربية", IsRightToLeft: true);

    /// <summary>Every language the app ships.</summary>
    public static IReadOnlyList<Language> All { get; } = [English, Arabic];
}

/// <summary>Looks up display text.</summary>
public interface ILocalizer : INotifyPropertyChanged
{
    /// <summary>The language in use.</summary>
    Language Current { get; }

    /// <summary>Which way the UI flows. Bound by the shell so the whole window mirrors.</summary>
    FlowDirection FlowDirection { get; }

    /// <summary>
    /// The text for a key. Indexer rather than a method so XAML can bind to it:
    /// <c>{Binding [Scan_Title], Source={StaticResource Loc}}</c>.
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// The text for a key with its <c>{0}</c> placeholders filled in.
    /// </summary>
    /// <remarks>
    /// Needed because a template like <c>"Re-apply {0}"</c> bound straight to a XAML
    /// <c>Content</c> renders the braces literally. Anything with a placeholder has to come
    /// through here.
    /// </remarks>
    string Format(string key, params object?[] arguments);

    /// <summary>Switches language. Every binding refreshes.</summary>
    void Use(Language language);
}

/// <summary>
/// Text from embedded JSON language packs.
/// </summary>
/// <remarks>
/// JSON rather than .resx on purpose. Two things had to be true: the app must switch language at
/// runtime without a restart, and a test must be able to prove both packs are complete. A plain
/// dictionary does both — comparing two key sets is trivial, and raising
/// <see cref="INotifyPropertyChanged"/> for the indexer re-evaluates every binding at once.
/// <para>
/// A missing key returns a visible marker rather than an empty string, so a gap shows up on
/// screen during testing instead of quietly rendering as blank.
/// </para>
/// </remarks>
public sealed class Localizer : ILocalizer
{
    private const string ResourcePrefix = "SystevoTune.App.Localization.Strings.";

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _packs;

    /// <summary>Loads the packs embedded in the app assembly.</summary>
    public Localizer()
        : this(LoadEmbeddedPacks())
    {
    }

    /// <summary>Loads from supplied packs. Used by tests.</summary>
    public Localizer(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);

        _packs = packs;
        Current = Language.English;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public Language Current { get; private set; }

    /// <inheritdoc />
    public FlowDirection FlowDirection => Current.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <inheritdoc />
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (_packs.TryGetValue(Current.Code, out var pack) && pack.TryGetValue(key, out var text))
            {
                return text;
            }

            // Fall back to English before giving up — a half-translated pack should still be
            // usable rather than a screen full of markers.
            if (_packs.TryGetValue(Language.English.Code, out var english) && english.TryGetValue(key, out var fallback))
            {
                return fallback;
            }

            return $"!{key}!";
        }
    }

    /// <inheritdoc />
    public string Format(string key, params object?[] arguments)
    {
        var template = this[key];

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, arguments ?? []);
        }
        catch (FormatException)
        {
            // A translation with a mangled placeholder must not take a screen down. Show the
            // template — visibly wrong, and the completeness tests exist to stop it reaching here.
            return template;
        }
    }

    /// <inheritdoc />
    public void Use(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        if (language.Code == Current.Code)
        {
            return;
        }

        Current = language;

        // "Item[]" is the WPF convention for "every indexer binding is stale".
        Raise("Item[]");
        Raise(nameof(Current));
        Raise(nameof(FlowDirection));
    }

    /// <summary>The keys a pack defines. Used by the completeness test.</summary>
    public IReadOnlyCollection<string> KeysFor(string languageCode)
        => _packs.TryGetValue(languageCode, out var pack) ? pack.Keys.ToList() : [];

    /// <summary>Language codes with a pack. Used by the completeness test.</summary>
    public IReadOnlyCollection<string> PackCodes => _packs.Keys.ToList();

    /// <summary>Reads every <c>Strings/*.json</c> embedded in the app assembly.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadEmbeddedPacks()
    {
        var assembly = typeof(Localizer).GetTypeInfo().Assembly;
        var packs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                                    && name.EndsWith(".json", StringComparison.Ordinal)))
        {
            var code = name[ResourcePrefix.Length..^".json".Length];

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);

            var pack = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
                ?? throw new InvalidOperationException($"Language pack '{code}' could not be read.");

            packs[code] = pack;
        }

        if (packs.Count == 0)
        {
            throw new InvalidOperationException("No language packs are embedded in the build.");
        }

        return packs;
    }

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
