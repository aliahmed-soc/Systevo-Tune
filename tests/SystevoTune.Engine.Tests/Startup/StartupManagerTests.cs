using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Startup;
using SystevoTune.TestSupport;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.Engine.Tests.Startup;

public class StartupManagerTests : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupFolder = @"C:\FakeUsers\tester\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup";

    private static readonly DateTimeOffset Noon = new(2026, 7, 26, 14, 0, 0, TimeSpan.Zero);

    private readonly TempLogDirectory _directory = new();
    private readonly FakeRegistryService _registry = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeEnvironmentPaths _environment = new();
    private readonly StartupManager _manager;
    private readonly TweakRunner _runner = new();

    public StartupManagerTests()
        => _manager = new StartupManager(
            StartupLocationCatalog.Load(), _registry, _files, _environment, new FixedClock(Noon));

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog()
        => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero)));

    private static RegistryValueRef Run(string name) => new(RegistryRoot.CurrentUser, RunKey, name);

    private static RegistryValueRef Approved(string name) => new(RegistryRoot.CurrentUser, ApprovedKey, name);

    private StartupItem Item(string name) => _manager.List().Single(item => item.Name == name);

    // ---- the catalogue ----

    [Fact]
    public void The_shipped_catalogue_covers_run_keys_and_startup_folders()
    {
        var kinds = StartupLocationCatalog.Load().Locations.Select(location => location.Kind).Distinct().ToList();

        Assert.Contains(StartupKind.RegistryRun, kinds);
        Assert.Contains(StartupKind.StartupFolder, kinds);
    }

    [Fact]
    public void No_location_points_the_engine_at_a_run_key_for_writing()
    {
        // Every approved key must be a StartupApproved key. Writing a Run key would mean the
        // engine could delete or rewrite the entry itself, which doc 3.2 forbids.
        Assert.All(StartupLocationCatalog.Load().Locations,
            location => Assert.Contains("StartupApproved", location.ApprovedKey, StringComparison.Ordinal));
    }

    [Fact]
    public void A_location_with_a_misspelt_root_is_caught_at_load()
    {
        const string json = """
            {"locations":[{"id":"a","nameEn":"A","nameAr":"أ","kind":"RegistryRun","root":"HKEY_CURRENT_USER",
             "key":"S","approvedRoot":"HKCU","approvedKey":"StartupApproved\\Run"}]}
            """;

        Assert.Throws<InvalidOperationException>(() => StartupLocationCatalog.Parse(json));
    }

    // ---- listing ----

    [Fact]
    public void Run_values_are_listed_with_their_command()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe /background"));

        var item = Assert.Single(_manager.List());

        Assert.Equal("OneDrive", item.Name);
        Assert.Equal(@"C:\OneDrive.exe /background", item.Command);
        Assert.Equal(StartupKind.RegistryRun, item.Kind);
    }

    [Fact]
    public void An_item_with_no_approval_value_counts_as_enabled()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));

        Assert.Equal(StartupState.Enabled, Assert.Single(_manager.List()).State);
    }

    [Fact]
    public void An_item_flagged_off_is_listed_as_disabled()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));
        _registry.With(Approved("OneDrive"), RegistryValue.Binary([0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

        Assert.Equal(StartupState.Disabled, Assert.Single(_manager.List()).State);
    }

    [Fact]
    public void Startup_folder_shortcuts_are_listed_too()
    {
        _files.WithFile($@"{StartupFolder}\Spotify.lnk", 1200);

        var item = Assert.Single(_manager.List());

        Assert.Equal("Spotify.lnk", item.Name);
        Assert.Equal(StartupKind.StartupFolder, item.Kind);
    }

    [Fact]
    public void A_startup_folder_that_does_not_exist_is_not_an_error()
        => Assert.Empty(_manager.List());

    [Fact]
    public void Items_from_different_locations_get_different_ids()
    {
        _registry.With(Run("Same"), RegistryValue.Text("a.exe"));
        _files.WithFile($@"{StartupFolder}\Same", 10);

        var ids = _manager.List().Select(item => item.Id).ToList();

        Assert.Equal(2, ids.Distinct().Count());
    }

    // ---- disable ----

    [Fact]
    public async Task Disabling_writes_only_the_approval_flag_and_never_the_run_value()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));
        var tweak = _manager.CreateTweak(Item("OneDrive"), StartupState.Disabled);

        await _runner.ApplyAsync([tweak], NewLog().StartRun());

        Assert.All(_registry.Writes, write => Assert.Contains("StartupApproved", write, StringComparison.Ordinal));
        Assert.Equal(RegistryValue.Text(@"C:\OneDrive.exe"), _registry.GetValue(Run("OneDrive")));
    }

    [Fact]
    public async Task A_disabled_item_still_exists_so_it_can_be_switched_back()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));
        await _runner.ApplyAsync([_manager.CreateTweak(Item("OneDrive"), StartupState.Disabled)], NewLog().StartRun());

        var item = Item("OneDrive");

        Assert.Equal(StartupState.Disabled, item.State);
        Assert.Equal(@"C:\OneDrive.exe", item.Command);
    }

    [Fact]
    public async Task Re_enabling_a_disabled_item_works()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));
        _registry.With(Approved("OneDrive"), RegistryValue.Binary([0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

        await _runner.ApplyAsync([_manager.CreateTweak(Item("OneDrive"), StartupState.Enabled)], NewLog().StartRun());

        Assert.Equal(StartupState.Enabled, Item("OneDrive").State);
    }

    [Fact]
    public async Task Disabling_something_already_off_has_nothing_to_do()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));
        _registry.With(Approved("OneDrive"), RegistryValue.Binary([0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

        var preview = await _runner.PreviewAsync([_manager.CreateTweak(Item("OneDrive"), StartupState.Disabled)]);

        Assert.Equal(TweakStatus.AlreadyApplied, Assert.Single(preview.Plans).Status);
    }

    [Fact]
    public void An_approval_value_is_twelve_bytes()
        => Assert.Equal(12, StartupManager.BuildApprovedValue(StartupState.Disabled, Noon).ToBytes().Length);

    [Fact]
    public void Disabling_stamps_the_filetime_windows_records_the_disable_time_in()
    {
        var bytes = StartupManager.BuildApprovedValue(StartupState.Disabled, Noon).ToBytes();

        Assert.Equal(0x03, bytes[0]);
        Assert.Equal(Noon.ToFileTime(), BitConverter.ToInt64(bytes, 4));
    }

    [Fact]
    public void Enabling_writes_a_zero_filetime_because_there_is_no_disable_time()
    {
        var bytes = StartupManager.BuildApprovedValue(StartupState.Enabled, Noon).ToBytes();

        Assert.Equal(0x02, bytes[0]);
        Assert.Equal(0, BitConverter.ToInt64(bytes, 4));
    }

    [Fact]
    public void The_flag_byte_0x06_is_read_as_enabled()
    {
        // Documented alongside 0x02 as an enabled marker. Reading it as disabled would make the
        // engine believe an item that still runs is already switched off.
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));
        _registry.With(Approved("OneDrive"), RegistryValue.Binary([0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

        Assert.Equal(StartupState.Enabled, Assert.Single(_manager.List()).State);
    }

    // ---- undo ----

    [Fact]
    public async Task Undo_switches_a_disabled_item_back_on()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));
        var log = NewLog();
        await _runner.ApplyAsync([_manager.CreateTweak(Item("OneDrive"), StartupState.Disabled)], log.StartRun());

        var undo = await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(StartupState.Enabled, Item("OneDrive").State);
    }

    [Fact]
    public async Task Undo_removes_the_approval_value_the_engine_created()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));
        var log = NewLog();
        await _runner.ApplyAsync([_manager.CreateTweak(Item("OneDrive"), StartupState.Disabled)], log.StartRun());

        await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.Null(_registry.GetValue(Approved("OneDrive")));
    }

    [Fact]
    public async Task Undo_restores_an_approval_value_that_was_already_there()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text(@"C:\OneDrive.exe"));
        var original = RegistryValue.Binary([0x02, 0, 0, 0, 0x77, 0, 0, 0, 0, 0, 0, 0]);
        _registry.With(Approved("OneDrive"), original);
        var log = NewLog();
        await _runner.ApplyAsync([_manager.CreateTweak(Item("OneDrive"), StartupState.Disabled)], log.StartRun());

        await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.Equal(original, _registry.GetValue(Approved("OneDrive")));
    }
}
