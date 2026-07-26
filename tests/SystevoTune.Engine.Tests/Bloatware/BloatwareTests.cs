using SystevoTune.Engine.Bloatware;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Platform.Windows;
using SystevoTune.Engine.Safety;
using SystevoTune.TestSupport;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.Engine.Tests.Bloatware;

public class BloatwareTests : IDisposable
{
    private const string ApprovedNews = """
        {"version":1,"packages":[
          {"name":"Microsoft.BingNews","nameEn":"Microsoft News","nameAr":"أخبار","approved":true,
           "whyEn":"News feed app."}]}
        """;

    private readonly TempLogDirectory _directory = new();
    private readonly FakeAppPackageService _packages = new();
    private readonly TweakRunner _runner = new();

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog()
        => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero)));

    private BloatwareModule Module(string json) => new(BloatwareWhitelist.Parse(json), _packages);

    // ---- the shipped list removes nothing until approved ----

    [Fact]
    public void The_shipped_whitelist_has_nothing_approved()
        => Assert.Empty(BloatwareWhitelist.Load().Approved);

    [Fact]
    public async Task An_unapproved_entry_produces_no_tweak_even_when_installed()
    {
        _packages.With("Microsoft.BingNews");

        var tweaks = await new BloatwareModule(BloatwareWhitelist.Load(), _packages).CreateTweaksAsync();

        Assert.Empty(tweaks);
    }

    [Fact]
    public void Every_shipped_entry_carries_both_names_and_a_reason()
        => Assert.All(BloatwareWhitelist.Load().Packages, package =>
        {
            Assert.False(string.IsNullOrWhiteSpace(package.NameAr));
            Assert.False(string.IsNullOrWhiteSpace(package.WhyEn));
        });

    // ---- the guard: parts of Windows are never removable ----

    [Theory]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.SecHealthUI")]
    [InlineData("Microsoft.VCLibs.140.00")]
    [InlineData("Microsoft.NET.Native.Framework.2.2")]
    [InlineData("Microsoft.UI.Xaml.2.8")]
    [InlineData("Microsoft.Windows.ShellExperienceHost")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    public void A_package_that_is_part_of_windows_is_refused_at_load(string name)
    {
        var json = $$"""
            {"version":1,"packages":[{"name":"{{name}}","nameEn":"X","nameAr":"س","approved":true}]}
            """;

        var error = Assert.Throws<InvalidOperationException>(() => BloatwareWhitelist.Parse(json));

        Assert.Contains("part of Windows itself", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_shipped_entry_is_a_part_of_windows()
        => Assert.All(
            BloatwareWhitelist.Load().Packages,
            package => Assert.False(BloatwareWhitelist.IsForbidden(package.Name)));

    [Fact]
    public void A_duplicate_entry_is_refused()
    {
        const string json = """
            {"version":1,"packages":[
              {"name":"Microsoft.BingNews","nameEn":"A","nameAr":"أ","approved":false},
              {"name":"microsoft.bingnews","nameEn":"B","nameAr":"ب","approved":false}]}
            """;

        Assert.Throws<InvalidOperationException>(() => BloatwareWhitelist.Parse(json));
    }

    // ---- preview and apply ----

    [Fact]
    public async Task An_approved_package_that_is_not_installed_produces_no_tweak()
        => Assert.Empty(await Module(ApprovedNews).CreateTweaksAsync());

    [Fact]
    public async Task The_preview_says_undo_usually_needs_the_store()
    {
        _packages.With("Microsoft.BingNews");

        var preview = await _runner.PreviewAsync(await Module(ApprovedNews).CreateTweaksAsync());

        Assert.Contains("Microsoft Store", Assert.Single(preview.AllChanges).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_removes_nothing()
    {
        _packages.With("Microsoft.BingNews");

        await _runner.PreviewAsync(await Module(ApprovedNews).CreateTweaksAsync());

        Assert.Empty(_packages.Removed);
    }

    [Fact]
    public async Task Applying_removes_the_package_and_records_its_full_identity()
    {
        _packages.With("Microsoft.BingNews", "Microsoft.BingNews_4.55.1_x64__8wekyb3d8bbwe");
        var log = NewLog();
        var run = log.StartRun();

        await _runner.ApplyAsync(await Module(ApprovedNews).CreateTweaksAsync(), run);

        Assert.Equal(["Microsoft.BingNews"], _packages.Removed);
        var record = Assert.Single(log.ReadRun(run.RunId).Records);
        Assert.Equal("Microsoft.BingNews", record.Target);
        // The versioned identity is what gives undo any chance at all.
        Assert.Equal("Microsoft.BingNews_4.55.1_x64__8wekyb3d8bbwe", record.OldValue);
        Assert.True(record.Undoable);
    }

    [Fact]
    public async Task A_removal_windows_refuses_is_reported_and_the_run_continues()
    {
        _packages.With("Microsoft.BingNews");
        _packages.RefusesToRemove.Add("Microsoft.BingNews");

        var report = await _runner.ApplyAsync(await Module(ApprovedNews).CreateTweaksAsync(), NewLog().StartRun());

        Assert.Contains("would not remove", Assert.Single(report.AllFailures).Reason, StringComparison.Ordinal);
    }

    // ---- undo: honest about failing ----

    [Fact]
    public async Task Undo_reinstalls_when_the_files_survived()
    {
        _packages.With("Microsoft.BingNews");
        _packages.CanReinstall.Add("Microsoft.BingNews");
        var log = NewLog();
        await _runner.ApplyAsync(await Module(ApprovedNews).CreateTweaksAsync(), log.StartRun());

        var undo = await new UndoEngine(log, [new BloatwareUndoHandler(_packages)]).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.True(_packages.IsInstalled("Microsoft.BingNews"));
    }

    [Fact]
    public async Task Undo_that_cannot_reinstall_says_where_to_get_the_app_back()
    {
        // The normal case: removal took the files, so only the Store can help. Saying "undo
        // failed" and stopping there would leave the user stuck.
        _packages.With("Microsoft.BingNews");
        var log = NewLog();
        await _runner.ApplyAsync(await Module(ApprovedNews).CreateTweaksAsync(), log.StartRun());

        var undo = await new UndoEngine(log, [new BloatwareUndoHandler(_packages)]).UndoAllAsync();

        var failure = Assert.Single(undo.Failures);
        Assert.Contains("Microsoft Store", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("deletes the files", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_reinstall_stays_pending_so_a_later_retry_can_work()
    {
        _packages.With("Microsoft.BingNews");
        var log = NewLog();
        await _runner.ApplyAsync(await Module(ApprovedNews).CreateTweaksAsync(), log.StartRun());
        await new UndoEngine(log, [new BloatwareUndoHandler(_packages)]).UndoAllAsync();

        // The user installs it from the Store, then runs Undo again.
        _packages.CanReinstall.Add("Microsoft.BingNews");
        var retry = await new UndoEngine(log, [new BloatwareUndoHandler(_packages)]).UndoAllAsync();

        Assert.True(retry.AllSucceeded);
    }

    // ---- Get-AppxPackage JSON parsing ----

    [Fact]
    public void A_single_package_comes_back_as_an_object_not_an_array()
    {
        const string json = """
            {"Name":"Microsoft.BingNews","PackageFullName":"Microsoft.BingNews_1_x64__abc","InstallLocation":"C:\\Apps\\News"}
            """;

        var package = Assert.Single(PowerShellAppPackageService.Parse(json));

        Assert.Equal("Microsoft.BingNews", package.Name);
        Assert.Equal(@"C:\Apps\News", package.InstallLocation);
    }

    [Fact]
    public void Several_packages_come_back_as_an_array()
    {
        const string json = """
            [{"Name":"A","PackageFullName":"A_1","InstallLocation":"C:\\A"},
             {"Name":"B","PackageFullName":"B_1","InstallLocation":null}]
            """;

        Assert.Equal(2, PowerShellAppPackageService.Parse(json).Count);
    }

    [Fact]
    public void An_entry_with_no_identity_is_skipped_rather_than_half_read()
        => Assert.Empty(PowerShellAppPackageService.Parse("""{"Name":"A","InstallLocation":"C:\\A"}"""));

    [Fact]
    public void Unreadable_output_yields_no_packages_rather_than_throwing()
    {
        Assert.Empty(PowerShellAppPackageService.Parse("not json"));
        Assert.Empty(PowerShellAppPackageService.Parse(string.Empty));
    }
}
