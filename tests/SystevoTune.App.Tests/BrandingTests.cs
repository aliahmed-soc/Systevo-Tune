using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using SystevoTune.App.Localization;

namespace SystevoTune.App.Tests;

/// <summary>
/// The Systevo brand assets, taken from systevo.vercel.app.
/// </summary>
/// <remarks>
/// Worth testing because the failure mode is silent: a missing or renamed image does not break
/// the build, it just renders as a blank box on someone's screen — and this app is never launched
/// in this environment, so nobody would see it until the VM run.
/// </remarks>
public partial class BrandingTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Packs =
        Localizer.LoadEmbeddedPacks();

    [Fact]
    public void The_logo_and_icon_are_in_the_repository()
    {
        Assert.True(File.Exists(AssetPath("systevo-logo.png")), "systevo-logo.png is missing");
        Assert.True(File.Exists(AssetPath("systevo.ico")), "systevo.ico is missing");
    }

    [Fact]
    public void The_logo_is_a_real_png_at_a_usable_size()
    {
        var bytes = File.ReadAllBytes(AssetPath("systevo-logo.png"));

        // PNG signature.
        Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47], bytes[..4]);

        // Width and height are big-endian at IHDR. 48px is the largest place it is shown.
        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];

        Assert.Equal(width, height);
        Assert.True(width >= 48, $"The logo is {width}px, smaller than the 48px it is drawn at");
    }

    [Fact]
    public void The_icon_carries_the_sizes_windows_asks_for()
    {
        var bytes = File.ReadAllBytes(AssetPath("systevo.ico"));

        Assert.Equal<byte[]>([0x00, 0x00, 0x01, 0x00], bytes[..4]);

        var count = bytes[4] | (bytes[5] << 8);
        var sizes = Enumerable.Range(0, count)
            .Select(i => bytes[6 + (i * 16)] == 0 ? 256 : bytes[6 + (i * 16)])
            .ToList();

        // 16 is the title bar, 32 is the taskbar. Without both, Windows scales one and it shows.
        Assert.Contains(16, sizes);
        Assert.Contains(32, sizes);
    }

    [Fact]
    public void The_logo_actually_decodes()
    {
        // The bytes being a PNG is not the same as WPF being able to draw it.
        var image = new BitmapImage(new Uri(AssetPath("systevo-logo.png")));

        Assert.True(image.PixelWidth > 0);
        Assert.True(image.PixelHeight > 0);
    }

    [Fact]
    public void The_window_and_the_project_both_point_at_the_icon()
    {
        Assert.Contains(
            "Icon=\"Assets/systevo.ico\"",
            File.ReadAllText(AppFile("MainWindow.xaml")),
            StringComparison.Ordinal);

        Assert.Contains(
            "<ApplicationIcon>Assets\\systevo.ico</ApplicationIcon>",
            File.ReadAllText(AppFile("SystevoTune.App.csproj")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_logo_is_pinned_left_to_right_so_arabic_does_not_mirror_it()
    {
        // The window flips FlowDirection wholesale for Arabic. Text should mirror; a logo is
        // artwork and should not.
        foreach (var file in new[] { AppFile("MainWindow.xaml"), AppFile(@"Views\SettingsView.xaml") })
        {
            var xaml = File.ReadAllText(file);
            var logoAt = xaml.IndexOf("systevo-logo.png", StringComparison.Ordinal);

            Assert.True(logoAt >= 0, $"{Path.GetFileName(file)} does not show the logo");

            // Look inside the <Image …> element the source sits in.
            var elementEnd = xaml.IndexOf("/>", logoAt, StringComparison.Ordinal);
            Assert.Contains(
                "FlowDirection=\"LeftToRight\"",
                xaml[logoAt..elementEnd],
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_copyright_reaches_the_shipped_assembly_metadata()
    {
        // What Windows shows in the exe's file properties. Doc 08: an unsigned system tool should
        // be as identifiable as it can be.
        var props = File.ReadAllText(RepoFile("Directory.Build.props"));

        Assert.Contains("<Copyright>© 2026 Systevo</Copyright>", props, StringComparison.Ordinal);
        Assert.Contains("<Company>Systevo</Company>", props, StringComparison.Ordinal);
    }

    [Fact]
    public void The_brand_strings_exist_in_both_languages()
    {
        foreach (var key in new[] { "App_Copyright", "App_Brand", "App_BrandTagline" })
        {
            Assert.False(string.IsNullOrWhiteSpace(Packs["en"][key]), $"en/{key}");
            Assert.False(string.IsNullOrWhiteSpace(Packs["ar"][key]), $"ar/{key}");
        }
    }

    [Fact]
    public void The_arabic_brand_tagline_is_actually_arabic()
    {
        // Guards against the tagline being left as the English one, which the copied-English
        // check exempts brand keys from.
        Assert.Matches(@"[؀-ۿ]", Packs["ar"]["App_BrandTagline"]);
    }

    [Fact]
    public void Every_asset_the_xaml_loads_is_embedded_as_a_resource()
    {
        // The first ever launch of this app died instantly:
        //     IOException: Cannot locate resource 'assets/systevo.ico'
        //         at SystevoTune.App.MainWindow.InitializeComponent()
        //
        // MainWindow.xaml sets Icon="Assets/systevo.ico", but the csproj declared only the PNG as
        // a <Resource>. ApplicationIcon is a different mechanism — it stamps the exe's Win32 icon
        // and embeds nothing WPF can load at runtime.
        //
        // The build succeeded, the portable publish succeeded, CI was green and nine branding
        // tests passed, because every one of those tests checked that a *string* was present
        // somewhere. None checked that the resource actually resolves. This one compares what the
        // XAML loads against what the project embeds, which is the gap the crash fell through.
        var declared = ResourceInclude()
            .Matches(File.ReadAllText(AppFile("SystevoTune.App.csproj")))
            .Select(match => match.Groups["path"].Value.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(AppFile(string.Empty), "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match use in XamlAssetUse().Matches(File.ReadAllText(file)))
            {
                var asset = use.Groups["path"].Value;

                if (!declared.Contains(asset))
                {
                    missing.Add($"{Path.GetFileName(file)} loads '{asset}' but no <Resource Include> declares it");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "An asset is referenced from XAML but not embedded, so the app will throw on startup:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, missing));
    }

    [GeneratedRegex(@"<Resource\s+Include=""(?<path>[^""]+)""", RegexOptions.ExplicitCapture)]
    private static partial Regex ResourceInclude();

    // Only real attribute usages — a path mentioned inside an XAML comment is not a load.
    [GeneratedRegex(@"(?:Icon|Source)=""(?<path>Assets/[^""]+)""", RegexOptions.ExplicitCapture)]
    private static partial Regex XamlAssetUse();

    private static string AssetPath(string name) => AppFile(Path.Combine("Assets", name));

    private static string AppFile(string relative) => RepoFile(Path.Combine("src", "SystevoTune.App", relative));

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative);
    }
}
