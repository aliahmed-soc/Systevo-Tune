using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;
using SystevoTune.TestSupport;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.Engine.Tests.Tweaks;

/// <summary>Doc 3.9: telemetry down, and ads and tips out of Start and the lock screen.</summary>
public class PrivacyTweakTests : IDisposable
{
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private readonly TempLogDirectory _directory = new();
    private readonly FakeRegistryService _registry = new();
    private readonly RegistryTweakCatalog _catalog = RegistryTweakCatalog.Load();
    private readonly TweakRunner _runner = new();

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog()
        => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero)));

    private RegistryTweak Tweak(string id)
        => new(_registry, _catalog.Find(id) ?? throw new InvalidOperationException($"no tweak '{id}'"));

    // ---- telemetry: honest about what it can actually do ----

    [Fact]
    public void Telemetry_writes_basic_not_off_because_off_is_enterprise_only()
    {
        // Microsoft: value 0 "is only applicable to Windows 10 Enterprise, Education... Using this
        // setting on other devices is equivalent to setting the value of 1." Writing 0 on a Home
        // PC and calling it "telemetry off" would be a claim we cannot keep.
        var value = Assert.Single(_catalog.Find("privacy.telemetry-minimal")!.Values);

        Assert.Equal("1", value.Data);
        Assert.Equal(RegistryValueType.Dword, value.Type);
    }

    [Fact]
    public void The_telemetry_tweak_name_does_not_promise_telemetry_is_off()
    {
        var name = _catalog.Find("privacy.telemetry-minimal")!.NameEn;

        Assert.DoesNotContain("off", name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required only", name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Telemetry_uses_the_documented_policy_key()
    {
        var reference = Assert.Single(_catalog.Find("privacy.telemetry-minimal")!.Values).ToRef();

        Assert.Equal(RegistryRoot.LocalMachine, reference.Root);
        Assert.Equal(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", reference.KeyPath);
        Assert.Equal("AllowTelemetry", reference.ValueName);
    }

    [Fact]
    public async Task A_pc_already_on_required_only_has_nothing_to_do()
    {
        _registry.With(
            _catalog.Find("privacy.telemetry-minimal")!.Values[0].ToRef(),
            RegistryValue.Dword(1));

        var preview = await _runner.PreviewAsync([Tweak("privacy.telemetry-minimal")]);

        Assert.Equal(TweakStatus.AlreadyApplied, Assert.Single(preview.Plans).Status);
    }

    [Fact]
    public async Task Turning_telemetry_down_from_full_is_logged_with_the_old_level()
    {
        _registry.With(_catalog.Find("privacy.telemetry-minimal")!.Values[0].ToRef(), RegistryValue.Dword(3));
        var log = NewLog();
        var run = log.StartRun();

        await _runner.ApplyAsync([Tweak("privacy.telemetry-minimal")], run);

        var record = Assert.Single(log.ReadRun(run.RunId).Records);
        Assert.Equal("Dword:3", record.OldValue);
        Assert.Equal("Dword:1", record.NewValue);
    }

    // ---- Start menu and lock screen ----

    [Fact]
    public void Every_suggestion_value_lives_under_content_delivery_manager()
    {
        var values = _catalog.Find("privacy.start-menu-suggestions-off")!.Values
            .Concat(_catalog.Find("privacy.tips-and-lock-screen-ads-off")!.Values);

        Assert.All(values, value =>
        {
            Assert.Equal(RegistryRoot.CurrentUser, value.ToRef().Root);
            Assert.Equal(ContentDelivery, value.ToRef().KeyPath);
            Assert.Equal("0", value.Data);
        });
    }

    [Fact]
    public void The_app_push_value_is_covered_because_it_installs_software_unasked()
    {
        var names = _catalog.Find("privacy.start-menu-suggestions-off")!.Values.Select(value => value.Name);

        Assert.Contains("SilentInstalledAppsEnabled", names);
    }

    [Fact]
    public void The_spotlight_wallpaper_itself_is_left_alone()
    {
        // RotatingLockScreenEnabled is the picture the user chose. Only the overlay text goes.
        var names = _catalog.Find("privacy.tips-and-lock-screen-ads-off")!.Values
            .Select(value => value.Name)
            .ToList();

        Assert.Contains("RotatingLockScreenOverlayEnabled", names);
        Assert.DoesNotContain("RotatingLockScreenEnabled", names);
    }

    // ---- the usual guarantees ----

    [Fact]
    public async Task Preview_writes_nothing()
    {
        await _runner.PreviewAsync(
            [Tweak("privacy.telemetry-minimal"), Tweak("privacy.start-menu-suggestions-off")]);

        Assert.Empty(_registry.Writes);
    }

    [Fact]
    public async Task Undo_puts_every_privacy_value_back_exactly()
    {
        var entry = _catalog.Find("privacy.tips-and-lock-screen-ads-off")!;
        _registry.With(entry.Values[0].ToRef(), RegistryValue.Dword(1));
        _registry.With(entry.Values[1].ToRef(), RegistryValue.Dword(1));
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("privacy.tips-and-lock-screen-ads-off")], log.StartRun());

        var undo = await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(RegistryValue.Dword(1), _registry.GetValue(entry.Values[0].ToRef()));
        Assert.Equal(RegistryValue.Dword(1), _registry.GetValue(entry.Values[1].ToRef()));
        // Never set before, so undo removes it rather than leaving a zero behind.
        Assert.Null(_registry.GetValue(entry.Values[2].ToRef()));
    }

    [Fact]
    public async Task Undo_removes_the_telemetry_policy_so_group_policy_goes_back_to_not_configured()
    {
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("privacy.telemetry-minimal")], log.StartRun());

        await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.Null(_registry.GetValue(_catalog.Find("privacy.telemetry-minimal")!.Values[0].ToRef()));
    }
}
