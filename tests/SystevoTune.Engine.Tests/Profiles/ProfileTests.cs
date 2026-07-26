using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tests.Fakes;
using SystevoTune.Engine.Tests.Safety;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.Engine.Tests.Profiles;

public class ProfileTests : IDisposable
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid High = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private const string UserTemp = @"C:\FakeUsers\tester\AppData\Local\Temp";

    private readonly TempLogDirectory _directory = new();
    private readonly FakeRegistryService _registry = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeEnvironmentPaths _environment = new();
    private readonly FakePowerPlanService _powerPlans = new();
    private readonly FakeBatteryStatus _battery = new();
    private readonly ProfileCatalog _profiles = ProfileCatalog.Load();
    private readonly RegistryTweakCatalog _registryTweaks = RegistryTweakCatalog.Load();
    private readonly PowerPlanCatalog _powerPlanCatalog = PowerPlanCatalog.Load();
    private readonly TweakRunner _runner = new();

    public void Dispose() => _directory.Dispose();

    private ProfileBuilder Builder() => new(
        new CleanupModule(CleanupWhitelist.Load(), _files, _environment),
        _registryTweaks,
        _registry,
        _powerPlans,
        _powerPlanCatalog,
        _battery);

    private ChangeLog NewLog()
        => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero)));

    // ---- the shipped profiles ----

    [Fact]
    public void Both_presets_ship()
    {
        Assert.NotNull(_profiles.Find("gaming"));
        Assert.NotNull(_profiles.Find("work"));
    }

    [Fact]
    public void Both_presets_have_arabic_names_and_descriptions()
        => Assert.All(_profiles.Profiles, profile =>
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.NameAr));
            Assert.False(string.IsNullOrWhiteSpace(profile.DescriptionAr));
        });

    [Fact]
    public void Every_step_in_every_preset_resolves_to_a_real_tweak()
    {
        // The point of this test: a typo in a profile file is a build-time failure, not a
        // surprise halfway through an apply run on someone's PC.
        foreach (var profile in _profiles.Profiles)
        {
            Assert.NotEmpty(Builder().Build(profile));
        }
    }

    [Fact]
    public void Steps_become_tweaks_in_file_order()
    {
        var tweaks = Builder().Build(_profiles.Find("gaming")!).Select(tweak => tweak.Id).ToList();

        Assert.Equal(
            [
                "cleanup.temp-files",
                "cleanup.recycle-bin",
                "power-plan.ultimate-performance",
                "visual-effects.performance",
                "game-mode.on",
                "game-bar.background-recording-off",
                "gpu-scheduling.on",
                "privacy.telemetry-minimal",
                "privacy.start-menu-suggestions-off",
                "privacy.tips-and-lock-screen-ads-off",
            ],
            tweaks);
    }

    [Fact]
    public void Both_presets_carry_the_privacy_tweaks_doc_3_says_yes_to()
    {
        // Doc 03's summary table: privacy tweaks are Yes for Gaming and Yes for Work.
        foreach (var profile in _profiles.Profiles)
        {
            var ids = Builder().Build(profile).Select(tweak => tweak.Id).ToList();

            Assert.Contains("privacy.telemetry-minimal", ids);
            Assert.Contains("privacy.start-menu-suggestions-off", ids);
            Assert.Contains("privacy.tips-and-lock-screen-ads-off", ids);
        }
    }

    [Fact]
    public void No_preset_cleans_the_windows_update_cache()
    {
        // Decision 23. Microsoft's documented reset stops wuauserv and BITS before touching that
        // folder, and a staged update awaiting restart can be deletable but still needed. The
        // group stays in the whitelist for explicit ticking; no preset does it blindly.
        foreach (var profile in _profiles.Profiles)
        {
            Assert.DoesNotContain("cleanup.windows-update-cache", Builder().Build(profile).Select(tweak => tweak.Id));
        }
    }

    [Fact]
    public void The_update_cache_group_is_still_available_to_tick_by_hand()
        => Assert.NotNull(CleanupWhitelist.Load().Find("windows-update-cache"));

    [Fact]
    public void The_gaming_preset_matches_the_doc_3_summary_table()
    {
        var ids = Builder().Build(_profiles.Find("gaming")!).Select(tweak => tweak.Id).ToList();

        Assert.Contains("visual-effects.performance", ids);
        Assert.Contains("game-mode.on", ids);
        Assert.Contains("game-bar.background-recording-off", ids);
        Assert.Contains("gpu-scheduling.on", ids);
        Assert.Contains("cleanup.temp-files", ids);
    }

    [Fact]
    public void The_work_preset_keeps_effects_on_and_game_mode_off()
    {
        var ids = Builder().Build(_profiles.Find("work")!).Select(tweak => tweak.Id).ToList();

        Assert.Contains("visual-effects.appearance", ids);
        Assert.Contains("game-mode.off", ids);
        Assert.Contains("power-plan.balanced", ids);
        Assert.DoesNotContain("gpu-scheduling.on", ids);
    }

    [Fact]
    public void No_preset_touches_startup_items()
    {
        // Decision 17: which apps to cut is per-person, so profiles never do it blindly.
        foreach (var profile in _profiles.Profiles)
        {
            Assert.DoesNotContain(Builder().Build(profile), tweak => tweak.Id.StartsWith("startup.", StringComparison.Ordinal));
        }
    }

    // ---- bad profiles ----

    [Fact]
    public void A_step_naming_a_tweak_that_does_not_exist_fails_before_anything_runs()
    {
        var profile = ProfileCatalog.Parse("""
            {"id":"bad","nameEn":"Bad","nameAr":"سيئ",
             "steps":[{"kind":"registry","id":"make-it-fast"}]}
            """);

        var error = Assert.Throws<InvalidOperationException>(() => Builder().Build(profile));

        Assert.Contains("not in the registry whitelist", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cleanup_step_naming_an_unknown_group_fails()
    {
        var profile = ProfileCatalog.Parse("""
            {"id":"bad","nameEn":"Bad","nameAr":"سيئ","steps":[{"kind":"cleanup","id":"everything"}]}
            """);

        Assert.Throws<InvalidOperationException>(() => Builder().Build(profile));
    }

    [Fact]
    public void A_power_plan_step_with_no_plans_fails()
    {
        var profile = ProfileCatalog.Parse("""
            {"id":"bad","nameEn":"Bad","nameAr":"سيئ","steps":[{"kind":"powerPlan","preferred":[]}]}
            """);

        Assert.Throws<InvalidOperationException>(() => Builder().Build(profile));
    }

    [Fact]
    public void A_profile_with_no_steps_is_refused()
        => Assert.Throws<InvalidOperationException>(() => ProfileCatalog.Parse(
            """{"id":"empty","nameEn":"Empty","nameAr":"فارغ","steps":[]}"""));

    // ---- power plan fallback, doc 3.4 ----

    [Fact]
    public async Task Gaming_takes_ultimate_when_the_pc_has_it()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High").With(Ultimate, "Ultimate");

        await _runner.ApplyAsync(Builder().Build(_profiles.Find("gaming")!), NewLog().StartRun());

        Assert.Equal(Ultimate, _powerPlans.Active);
    }

    [Fact]
    public async Task Gaming_falls_back_to_high_when_ultimate_is_absent()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High");

        await _runner.ApplyAsync(Builder().Build(_profiles.Find("gaming")!), NewLog().StartRun());

        Assert.Equal(High, _powerPlans.Active);
    }

    // ---- the full round trip ----

    [Fact]
    public async Task Previewing_a_profile_changes_nothing()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High");
        _files.WithFile($@"{UserTemp}\a.tmp", 4096);

        var preview = await _runner.PreviewAsync(Builder().Build(_profiles.Find("gaming")!));

        Assert.NotEmpty(preview.AllChanges);
        Assert.Empty(_registry.Writes);
        Assert.Empty(_powerPlans.Activated);
        Assert.Empty(_files.Deleted);
        Assert.Empty(NewLog().ListRunIds());
    }

    [Fact]
    public async Task Applying_gaming_then_undoing_puts_every_setting_back()
    {
        // The doc 07.2 shape, in miniature: known state, apply everything, undo everything,
        // compare against what was there before.
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High");
        _registry.With(_registryTweaks.Find("visual-effects.performance")!.Values[0].ToRef(), RegistryValue.Dword(3));
        _files.WithFile($@"{UserTemp}\a.tmp", 4096);
        var log = NewLog();

        var apply = await _runner.ApplyAsync(Builder().Build(_profiles.Find("gaming")!), log.StartRun());
        var undo = await new UndoEngine(log, [new RegistryUndoHandler(_registry), new PowerPlanUndoHandler(_powerPlans)])
            .UndoAllAsync();

        Assert.True(apply.AllSucceeded);
        Assert.True(undo.AllSucceeded);
        Assert.Equal(Balanced, _powerPlans.Active);
        Assert.Equal(
            RegistryValue.Dword(3),
            _registry.GetValue(_registryTweaks.Find("visual-effects.performance")!.Values[0].ToRef()));
    }

    [Fact]
    public async Task Undo_after_a_profile_reports_the_deleted_files_as_permanent()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High");
        _files.WithFile($@"{UserTemp}\a.tmp", 4096);
        var log = NewLog();
        await _runner.ApplyAsync(Builder().Build(_profiles.Find("gaming")!), log.StartRun());

        var undo = await new UndoEngine(log, [new RegistryUndoHandler(_registry), new PowerPlanUndoHandler(_powerPlans)])
            .UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal("temp-files", Assert.Single(undo.Permanent).Target);
    }

    [Fact]
    public async Task Applying_gaming_then_work_then_undoing_still_lands_on_the_original_state()
    {
        // Doc 07.4: running apply twice must not confuse undo.
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High");
        _registry.With(_registryTweaks.Find("visual-effects.performance")!.Values[0].ToRef(), RegistryValue.Dword(3));
        var log = NewLog();

        await _runner.ApplyAsync(Builder().Build(_profiles.Find("gaming")!), log.StartRun());
        await _runner.ApplyAsync(Builder().Build(_profiles.Find("work")!), log.StartRun());
        var undo = await new UndoEngine(log, [new RegistryUndoHandler(_registry), new PowerPlanUndoHandler(_powerPlans)])
            .UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(Balanced, _powerPlans.Active);
        Assert.Equal(
            RegistryValue.Dword(3),
            _registry.GetValue(_registryTweaks.Find("visual-effects.performance")!.Values[0].ToRef()));
    }

    [Fact]
    public async Task Applying_the_same_profile_twice_leaves_nothing_to_do_the_second_time()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High");
        var log = NewLog();
        await _runner.ApplyAsync(Builder().Build(_profiles.Find("work")!), log.StartRun());

        var second = await _runner.PreviewAsync(Builder().Build(_profiles.Find("work")!));

        Assert.Empty(second.AllChanges);
    }
}
