using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Safety;
using SystevoTune.TestSupport;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.Engine.Tests.Cleanup;

public class CleanupModuleTests : IDisposable
{
    private const string Whitelist = """
        {"version":1,"groups":[
          {"id":"temp-files","nameEn":"Temporary files","nameAr":"الملفات المؤقتة","recursive":true,
           "paths":["{USER_TEMP}","{WINDIR}\\Temp"]},
          {"id":"windows-update-cache","nameEn":"Windows Update cache","nameAr":"ذاكرة التحديثات","recursive":true,
           "paths":["{WINDIR}\\SoftwareDistribution\\Download"]}]}
        """;

    private const string UserTemp = @"C:\FakeUsers\tester\AppData\Local\Temp";
    private const string WindowsTemp = @"C:\FakeWindows\Temp";
    private const string UpdateCache = @"C:\FakeWindows\SoftwareDistribution\Download";

    private readonly TempLogDirectory _directory = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeEnvironmentPaths _environment = new();
    private readonly CleanupModule _module;

    public CleanupModuleTests()
        => _module = new CleanupModule(CleanupWhitelist.Parse(Whitelist), _files, _environment);

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog() => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero)));

    // ---- scan first ----

    [Fact]
    public void Scan_reports_size_per_group()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000).WithFile($@"{WindowsTemp}\b.log", 2000);
        _files.WithFile($@"{UpdateCache}\patch.cab", 5000);

        var report = _module.Scan();

        Assert.Equal(3000, report.Groups.Single(group => group.GroupId == "temp-files").TotalBytes);
        Assert.Equal(5000, report.Groups.Single(group => group.GroupId == "windows-update-cache").TotalBytes);
        Assert.Equal(8000, report.TotalBytes);
        Assert.Equal(3, report.TotalFiles);
    }

    [Fact]
    public void Scan_deletes_nothing()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000);

        _module.Scan();

        Assert.Empty(_files.Deleted);
        Assert.True(_files.Exists($@"{UserTemp}\a.tmp"));
    }

    [Fact]
    public void Scan_walks_subfolders()
    {
        _files.WithFile($@"{UserTemp}\deep\deeper\a.tmp", 1500);

        Assert.Equal(1500, _module.Scan(["temp-files"]).TotalBytes);
    }

    [Fact]
    public void A_whitelist_path_that_does_not_exist_is_reported_not_an_error()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000);

        var group = _module.Scan(["temp-files"]).Groups.Single();

        Assert.Equal(WindowsTemp, Assert.Single(group.MissingPaths));
        Assert.Empty(group.RejectedPaths);
    }

    [Fact]
    public void A_folder_the_user_cannot_open_is_skipped_rather_than_crashing_the_scan()
    {
        _files.WithFile($@"{UserTemp}\readable.tmp", 100);
        _files.WithFile($@"{UserTemp}\locked\secret.tmp", 999);
        _files.UnreadableDirectories.Add($@"{UserTemp}\locked");

        Assert.Equal(100, _module.Scan(["temp-files"]).TotalBytes);
    }

    [Fact]
    public void Scan_can_be_narrowed_to_chosen_groups()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000).WithFile($@"{UpdateCache}\patch.cab", 5000);

        var report = _module.Scan(["windows-update-cache"]);

        Assert.Equal("windows-update-cache", Assert.Single(report.Groups).GroupId);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void Sizes_are_shown_in_units_people_read(long bytes, string expected)
        => Assert.Equal(expected, CleanupScanReport.Humanise(bytes));

    // ---- preview ----

    [Fact]
    public async Task Preview_reports_one_change_per_group_and_deletes_nothing()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000).WithFile($@"{UpdateCache}\patch.cab", 5000);

        var preview = await new TweakRunner().PreviewAsync(_module.CreateTweaks());

        Assert.Equal(2, preview.AllChanges.Count);
        Assert.Empty(_files.Deleted);
    }

    [Fact]
    public async Task Preview_says_how_much_would_be_freed()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1536);

        var preview = await new TweakRunner().PreviewAsync(_module.CreateTweaks(["temp-files"]));

        Assert.Contains("1.5 KB", Assert.Single(preview.AllChanges).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_group_with_nothing_in_it_has_nothing_to_do()
    {
        _files.WithDirectory(UserTemp);

        var preview = await new TweakRunner().PreviewAsync(_module.CreateTweaks(["temp-files"]));

        Assert.Equal(TweakStatus.AlreadyApplied, Assert.Single(preview.Plans).Status);
        Assert.Empty(preview.AllChanges);
    }

    [Fact]
    public async Task A_whitelist_naming_a_user_folder_blocks_the_tweak_instead_of_deleting()
    {
        const string bad = """
            {"version":1,"groups":[{"id":"bad","nameEn":"Bad","nameAr":"سيئ",
             "paths":["C:\\FakeUsers\\tester\\Documents"]}]}
            """;
        var module = new CleanupModule(CleanupWhitelist.Parse(bad), _files, _environment);
        _files.WithFile(@"C:\FakeUsers\tester\Documents\thesis.docx", 90_000);

        var preview = await new TweakRunner().PreviewAsync(module.CreateTweaks());

        Assert.Equal(TweakStatus.Blocked, Assert.Single(preview.Plans).Status);
        Assert.Empty(_files.Deleted);
        Assert.True(_files.Exists(@"C:\FakeUsers\tester\Documents\thesis.docx"));
    }

    // ---- apply ----

    [Fact]
    public async Task Applying_deletes_the_files_and_logs_one_record_per_group()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000).WithFile($@"{UserTemp}\b.tmp", 2000);
        var log = NewLog();
        var run = log.StartRun();

        var report = await new TweakRunner().ApplyAsync(_module.CreateTweaks(["temp-files"]), run);

        Assert.True(report.AllSucceeded);
        Assert.False(_files.Exists($@"{UserTemp}\a.tmp"));
        var record = Assert.Single(log.ReadRun(run.RunId).Records);
        Assert.Equal("temp-files", record.Target);
        Assert.Equal("files=2;bytes=3000", record.OldValue);
    }

    [Fact]
    public async Task Cleanup_records_are_marked_as_permanent()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000);
        var log = NewLog();
        var run = log.StartRun();

        await new TweakRunner().ApplyAsync(_module.CreateTweaks(["temp-files"]), run);

        Assert.False(Assert.Single(log.ReadRun(run.RunId).Records).Undoable);
    }

    [Fact]
    public async Task A_locked_file_is_counted_and_the_rest_are_still_deleted()
    {
        _files.WithFile($@"{UserTemp}\open.tmp", 1000).WithFile($@"{UserTemp}\free.tmp", 2000);
        _files.LockedFiles.Add($@"{UserTemp}\open.tmp");
        var tweaks = _module.CreateTweaks(["temp-files"]);

        var report = await new TweakRunner().ApplyAsync(tweaks, NewLog().StartRun());

        Assert.True(report.AllSucceeded);
        var detail = ((CleanupTweak)tweaks[0]).LastApply!;
        Assert.Equal(1, detail.FilesDeleted);
        Assert.Equal(1, detail.FilesLocked);
        Assert.Equal(2000, detail.BytesFreed);
        Assert.True(_files.Exists($@"{UserTemp}\open.tmp"));
    }

    [Fact]
    public async Task Freed_size_matches_what_was_actually_removed()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000).WithFile($@"{UserTemp}\b.tmp", 2000);
        var tweaks = _module.CreateTweaks(["temp-files"]);

        await new TweakRunner().ApplyAsync(tweaks, NewLog().StartRun());

        Assert.Equal(3000, ((CleanupTweak)tweaks[0]).LastApply!.BytesFreed);
    }

    [Fact]
    public async Task Undo_reports_cleanup_as_permanent_rather_than_failing()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000);
        var log = NewLog();
        await new TweakRunner().ApplyAsync(_module.CreateTweaks(["temp-files"]), log.StartRun());

        var undo = await new UndoEngine(log, []).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(0, undo.AttemptedCount);
        Assert.Equal("temp-files", Assert.Single(undo.Permanent).Target);
    }

    [Fact]
    public async Task Nothing_outside_the_whitelist_is_ever_touched()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1000);
        _files.WithFile(@"C:\FakeUsers\tester\Documents\thesis.docx", 90_000);
        _files.WithFile(@"C:\FakeWindows\System32\kernel32.dll", 500_000);

        await new TweakRunner().ApplyAsync(_module.CreateTweaks(), NewLog().StartRun());

        Assert.Equal([$@"{UserTemp}\a.tmp"], _files.Deleted);
    }
}
