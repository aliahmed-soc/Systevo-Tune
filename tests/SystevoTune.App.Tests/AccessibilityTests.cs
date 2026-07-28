using System.IO;
using System.Text.RegularExpressions;

namespace SystevoTune.App.Tests;

/// <summary>
/// C1 and C2, checked rather than claimed.
/// </summary>
/// <remarks>
/// The app cannot be launched in this environment, so "we did an accessibility pass" would be an
/// assertion with nothing behind it. These read the XAML instead: a screen reader needs a name on
/// every control it can land on, and a keyboard user needs a tab order and a way out of a dialog.
/// </remarks>
public partial class AccessibilityTests
{
    private static readonly string[] InteractiveElements = ["Button", "CheckBox", "ComboBox", "TextBox", "ListBox"];

    // ---- C2: every interactive control is named ----

    [Fact]
    public void Every_interactive_control_has_an_automation_name()
    {
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (var element in Elements(File.ReadAllText(file)))
            {
                if (!InteractiveElements.Contains(element.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                // A control whose Content is already bound to localised text is announced from
                // that; the explicit name matters where Content is a template or a value.
                if (element.Text.Contains("AutomationProperties.Name", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{Path.GetFileName(file)}: <{element.Name} …>");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Interactive controls with no AutomationProperties.Name:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void The_scan_actually_reaches_the_apps_controls()
    {
        // Without this, a path change turns the check above into a test that passes because it
        // examined nothing — the worst kind of green.
        var found = XamlFiles()
            .SelectMany(file => Elements(File.ReadAllText(file)))
            .Count(element => InteractiveElements.Contains(element.Name, StringComparer.Ordinal));

        Assert.True(found >= 15, $"Only {found} interactive controls found across the app's XAML");
    }

    [Fact]
    public void The_scanner_can_tell_a_named_control_from_an_unnamed_one()
    {
        // Proves the check bites rather than passing because it finds nothing.
        var elements = Elements("""
            <Button Content="{Binding X}" />
            <Button Content="{Binding Y}" AutomationProperties.Name="{Binding Z}" />
            """).Where(e => e.Name == "Button").ToList();

        Assert.Equal(2, elements.Count);
        Assert.DoesNotContain("AutomationProperties.Name", elements[0].Text, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", elements[1].Text, StringComparison.Ordinal);
    }

    // ---- C1: keyboard ----

    [Fact]
    public void Every_screen_gives_its_controls_a_tab_order()
    {
        // Without TabIndex, WPF falls back to declaration order across the whole visual tree,
        // which puts the nav bar and the screen content in an order nobody chose.
        foreach (var file in XamlFiles().Where(f => Path.GetFileName(f) != "Dark.xaml"
                                                    && Path.GetFileName(f) != "App.xaml"))
        {
            var xaml = File.ReadAllText(file);

            if (!Elements(xaml).Any(e => InteractiveElements.Contains(e.Name, StringComparer.Ordinal)))
            {
                continue;
            }

            Assert.True(
                xaml.Contains("TabIndex", StringComparison.Ordinal),
                $"{Path.GetFileName(file)} has interactive controls but sets no TabIndex");
        }
    }

    [Fact]
    public void Tab_indexes_are_unique_within_a_screen()
    {
        // Duplicates make the order arbitrary again, which is the thing TabIndex was added to fix.
        foreach (var file in XamlFiles())
        {
            var indexes = TabIndexAttribute()
                .Matches(File.ReadAllText(file))
                .Select(match => match.Groups["value"].Value)
                .ToList();

            Assert.Equal(indexes.Count, indexes.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void The_confirm_dialog_has_a_default_and_a_cancel_button()
    {
        // IsCancel is what makes Esc close the dialog, and IsDefault is what makes Enter confirm.
        // On the one dialog that stands between a user and changing their PC, both matter.
        var xaml = File.ReadAllText(XamlFiles().Single(f => Path.GetFileName(f) == "ConfirmApplyDialog.xaml"));

        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_apply_button_is_the_default_action_on_the_review_screen()
    {
        var xaml = File.ReadAllText(XamlFiles().Single(f => Path.GetFileName(f) == "ReviewView.xaml"));

        Assert.Contains("IsDefault=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Stopping_a_run_is_reachable_with_escape()
    {
        var xaml = File.ReadAllText(XamlFiles().Single(f => Path.GetFileName(f) == "ApplyView.xaml"));

        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
    }

    // ---- C2: contrast on the dark theme ----

    [Theory]
    // Foreground against the card surface #1E1F24, which is what body text actually sits on.
    [InlineData("#ECEDEF", "Text")]
    [InlineData("#9EA3AD", "Muted")]
    [InlineData("#4C9AFF", "Accent")]
    [InlineData("#5BD08A", "Success")]
    [InlineData("#E8C468", "Warning")]
    [InlineData("#FF6B6B", "Danger")]
    public void Every_theme_foreground_clears_wcag_aa_on_the_card_surface(string foreground, string name)
    {
        var ratio = ContrastRatio(foreground, "#1E1F24");

        Assert.True(ratio >= 4.5, $"{name} ({foreground}) is {ratio:N2}:1 on the card surface, below AA's 4.5:1");
    }

    [Fact]
    public void The_contrast_maths_agrees_with_a_known_pair()
    {
        // Black on white is 21:1 by definition. If this drifts, every row above is meaningless.
        Assert.Equal(21.0, ContrastRatio("#000000", "#FFFFFF"), 1);
    }

    /// <summary>WCAG 2.1 relative luminance and contrast ratio.</summary>
    private static double ContrastRatio(string first, string second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        var (light, dark) = a > b ? (a, b) : (b, a);

        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(string hex)
    {
        var value = hex.TrimStart('#');
        var channels = new[]
        {
            Convert.ToInt32(value[..2], 16) / 255.0,
            Convert.ToInt32(value.Substring(2, 2), 16) / 255.0,
            Convert.ToInt32(value.Substring(4, 2), 16) / 255.0,
        };

        var linear = channels
            .Select(c => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4))
            .ToArray();

        return (0.2126 * linear[0]) + (0.7152 * linear[1]) + (0.0722 * linear[2]);
    }

    private static IEnumerable<(string Name, string Text)> Elements(string xaml)
        => ElementTag().Matches(xaml).Select(match => (match.Groups["name"].Value, match.Value));

    private static IReadOnlyList<string> XamlFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        var app = directory is null ? null : Path.Combine(directory.FullName, "src", "SystevoTune.App");
        return app is not null && Directory.Exists(app)
            ? Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>An opening tag and its attributes, up to the closing bracket.</summary>
    [GeneratedRegex(@"<(?<name>[A-Za-z]+)\b[^>]*>", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex ElementTag();

    [GeneratedRegex(@"TabIndex\s*=\s*""(?<value>\d+)""", RegexOptions.ExplicitCapture)]
    private static partial Regex TabIndexAttribute();
}
