using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using SystevoTune.App.Localization;

namespace SystevoTune.App.Tests;

/// <summary>
/// A7: every string in a resource file, both languages complete, and nothing hard-coded in XAML.
/// </summary>
public partial class LocalizationTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Packs =
        Localizer.LoadEmbeddedPacks();

    // ---- both packs complete ----

    [Fact]
    public void Both_languages_ship()
    {
        Assert.Contains("en", Packs.Keys);
        Assert.Contains("ar", Packs.Keys);
    }

    [Fact]
    public void Arabic_defines_every_key_english_does()
    {
        var missing = Packs["en"].Keys.Except(Packs["ar"].Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);

        Assert.Empty(missing);
    }

    [Fact]
    public void English_defines_every_key_arabic_does()
    {
        // The other direction matters too: an Arabic-only key is a string nobody can read in
        // English, and the fallback would show the raw key.
        var extra = Packs["ar"].Keys.Except(Packs["en"].Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);

        Assert.Empty(extra);
    }

    [Fact]
    public void No_value_in_either_pack_is_blank()
    {
        foreach (var (code, pack) in Packs)
        {
            foreach (var (key, value) in pack)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{code}/{key} is blank");
            }
        }
    }

    [Fact]
    public void Arabic_is_actually_translated_rather_than_copied_english()
    {
        // A pack that passes the completeness check by copying English would be worse than an
        // obvious gap, because nothing would flag it.
        var identical = Packs["en"]
            .Where(pair => Packs["ar"].TryGetValue(pair.Key, out var arabic)
                           && string.Equals(arabic, pair.Value, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(identical);
    }

    [Fact]
    public void Every_placeholder_survives_translation()
    {
        // "{0} of {1}" translated as "{0} فقط" would throw at format time on a real screen.
        foreach (var (key, english) in Packs["en"])
        {
            var expected = Placeholders(english);
            var actual = Placeholders(Packs["ar"][key]);

            Assert.True(expected.SetEquals(actual), $"{key}: en has {Show(expected)}, ar has {Show(actual)}");
        }
    }

    // ---- switching ----

    [Fact]
    public void The_app_starts_in_english_left_to_right()
    {
        var localizer = new Localizer(Packs);

        Assert.Equal("en", localizer.Current.Code);
        Assert.Equal(FlowDirection.LeftToRight, localizer.FlowDirection);
    }

    [Fact]
    public void Switching_to_arabic_mirrors_the_layout()
    {
        var localizer = new Localizer(Packs);

        localizer.Use(Language.Arabic);

        Assert.Equal(FlowDirection.RightToLeft, localizer.FlowDirection);
    }

    [Fact]
    public void Switching_language_tells_every_binding_to_re_read()
    {
        var localizer = new Localizer(Packs);
        var raised = new List<string?>();
        localizer.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        localizer.Use(Language.Arabic);

        // "Item[]" is what WPF listens for to re-evaluate indexer bindings.
        Assert.Contains("Item[]", raised);
        Assert.Contains(nameof(Localizer.FlowDirection), raised);
    }

    [Fact]
    public void Switching_to_the_language_already_in_use_changes_nothing()
    {
        var localizer = new Localizer(Packs);
        var raised = 0;
        localizer.PropertyChanged += (_, _) => raised++;

        localizer.Use(Language.English);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_key_that_does_not_exist_shows_a_visible_marker()
    {
        // A blank would look like a design choice; !Key! looks like the bug it is.
        var localizer = new Localizer(Packs);

        Assert.Equal("!Nope_NotAKey!", localizer["Nope_NotAKey"]);
    }

    [Fact]
    public void A_key_missing_only_from_arabic_falls_back_to_english()
    {
        var packs = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string> { ["A"] = "Apple" },
            ["ar"] = new Dictionary<string, string>(),
        };
        var localizer = new Localizer(packs);
        localizer.Use(Language.Arabic);

        Assert.Equal("Apple", localizer["A"]);
    }

    // ---- nothing hard-coded in XAML ----

    [Fact]
    public void No_xaml_file_carries_a_hard_coded_user_facing_literal()
    {
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            var xaml = File.ReadAllText(file);

            foreach (Match match in LiteralAttribute().Matches(xaml))
            {
                var value = match.Groups["value"].Value;

                // A binding is fine; so is an empty string.
                if (value.StartsWith('{') || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                offenders.Add($"{Path.GetFileName(file)}: {match.Groups["attr"].Value}=\"{value}\"");
            }
        }

        Assert.True(offenders.Count == 0, "Hard-coded UI text found:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void The_scanner_would_actually_catch_something()
    {
        // A scanner that can never fail is worse than no scanner, so prove it flags a literal and
        // leaves a binding alone — using the same filter the real check applies.
        const string xaml = """<TextBlock Text="Hello" /><Button Content="{Binding X}" /><TextBlock Text="" />""";

        var flagged = LiteralAttribute().Matches(xaml)
            .Select(match => match.Groups["value"].Value)
            .Where(value => !value.StartsWith('{') && !string.IsNullOrWhiteSpace(value))
            .ToList();

        Assert.Equal(["Hello"], flagged);
    }

    [Fact]
    public void The_scanner_is_looking_at_a_real_set_of_files()
    {
        // Guards against a path change silently turning this suite into a no-op.
        Assert.True(XamlFiles().Count >= 6, "Expected to find the app's XAML files");
    }

    private static IReadOnlyList<string> XamlFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return [];
        }

        var app = Path.Combine(directory.FullName, "src", "SystevoTune.App");
        return Directory.Exists(app) ? Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories) : [];
    }

    private static HashSet<string> Placeholders(string text)
        => Placeholder().Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    private static string Show(HashSet<string> values)
        => values.Count == 0 ? "none" : string.Join(",", values.Order(StringComparer.Ordinal));

    /// <summary>User-facing attributes that must never hold a literal.</summary>
    [GeneratedRegex(@"\s(?<attr>Text|Content|Header|ToolTip|Title)\s*=\s*""(?<value>[^""]*)""", RegexOptions.ExplicitCapture)]
    private static partial Regex LiteralAttribute();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
