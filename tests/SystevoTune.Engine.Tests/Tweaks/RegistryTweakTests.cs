using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tests.Fakes;
using SystevoTune.Engine.Tests.Safety;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.Engine.Tests.Tweaks;

public class RegistryTweakTests : IDisposable
{
    private readonly TempLogDirectory _directory = new();
    private readonly FakeRegistryService _registry = new();
    private readonly RegistryTweakCatalog _catalog = RegistryTweakCatalog.Load();
    private readonly TweakRunner _runner = new();

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog()
        => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero)));

    private RegistryTweak Tweak(string id)
        => new(_registry, _catalog.Find(id) ?? throw new InvalidOperationException($"no tweak '{id}'"));

    private static RegistryValueRef Ref(string id, int index)
        => RegistryTweakCatalog.Load().Find(id)!.Values[index].ToRef();

    // ---- the shipped catalogue ----

    [Fact]
    public void The_shipped_catalogue_covers_the_doc_3_5_and_3_6_tweaks()
    {
        var ids = _catalog.Tweaks.Select(tweak => tweak.Id).ToList();

        Assert.Contains("visual-effects.performance", ids);
        Assert.Contains("visual-effects.appearance", ids);
        Assert.Contains("game-mode.on", ids);
        Assert.Contains("game-bar.background-recording-off", ids);
        Assert.Contains("gpu-scheduling.on", ids);
    }

    [Fact]
    public void Every_shipped_tweak_has_an_arabic_name_and_at_least_one_value()
        => Assert.All(_catalog.Tweaks, tweak =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tweak.NameAr));
            Assert.NotEmpty(tweak.Values);
        });

    [Fact]
    public void No_shipped_tweak_touches_a_forbidden_area()
    {
        string[] forbidden = ["defender", "firewall", "mpssvc", "windefend", "audiosrv", "spooler", "dhcp", "dnscache"];

        foreach (var value in _catalog.Tweaks.SelectMany(tweak => tweak.Values))
        {
            var path = value.ToRef().ToString();
            Assert.DoesNotContain(forbidden, word => path.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void A_misspelt_root_is_caught_when_the_catalogue_loads()
    {
        const string json = """
            {"tweaks":[{"id":"a","nameEn":"A","nameAr":"أ",
             "values":[{"root":"HKEY_LOCAL_MACHINE","key":"Software\\X","name":"V","type":"Dword","data":"1"}]}]}
            """;

        Assert.Throws<InvalidOperationException>(() => RegistryTweakCatalog.Parse(json));
    }

    [Fact]
    public void A_duplicate_tweak_id_is_refused()
    {
        const string json = """
            {"tweaks":[
              {"id":"a","nameEn":"A","nameAr":"أ","values":[{"root":"HKCU","key":"S","name":"V","type":"Dword","data":"1"}]},
              {"id":"a","nameEn":"B","nameAr":"ب","values":[{"root":"HKCU","key":"S","name":"V","type":"Dword","data":"2"}]}]}
            """;

        Assert.Throws<InvalidOperationException>(() => RegistryTweakCatalog.Parse(json));
    }

    // ---- preview ----

    [Fact]
    public async Task Preview_reports_each_value_that_differs_and_writes_nothing()
    {
        _registry.With(Ref("visual-effects.performance", 0), RegistryValue.Dword(1));

        var preview = await _runner.PreviewAsync([Tweak("visual-effects.performance")]);

        Assert.Equal(3, preview.AllChanges.Count);
        Assert.Empty(_registry.Writes);
    }

    [Fact]
    public async Task A_value_already_at_the_target_is_left_out_of_the_plan()
    {
        _registry.With(Ref("visual-effects.performance", 0), RegistryValue.Dword(2));

        var preview = await _runner.PreviewAsync([Tweak("visual-effects.performance")]);

        Assert.DoesNotContain(preview.AllChanges, change => change.Target.Contains("VisualFXSetting", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tweak_whose_values_all_match_has_nothing_to_do()
    {
        var entry = _catalog.Find("visual-effects.performance")!;
        foreach (var value in entry.Values)
        {
            _registry.With(value.ToRef(), value.ToValue());
        }

        var preview = await _runner.PreviewAsync([Tweak("visual-effects.performance")]);

        Assert.Equal(TweakStatus.AlreadyApplied, Assert.Single(preview.Plans).Status);
    }

    [Fact]
    public async Task A_missing_value_shows_as_not_set_in_the_preview()
    {
        var preview = await _runner.PreviewAsync([Tweak("game-mode.on")]);

        Assert.Contains("(not set)", Assert.Single(preview.AllChanges).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gpu_scheduling_is_not_applicable_when_the_value_does_not_exist()
    {
        var preview = await _runner.PreviewAsync([Tweak("gpu-scheduling.on")]);

        Assert.Equal(TweakStatus.NotApplicable, Assert.Single(preview.Plans).Status);
        Assert.Empty(_registry.Writes);
    }

    [Fact]
    public async Task Gpu_scheduling_needs_a_restart_when_it_can_run()
    {
        _registry.With(Ref("gpu-scheduling.on", 0), RegistryValue.Dword(1));

        var preview = await _runner.PreviewAsync([Tweak("gpu-scheduling.on")]);

        Assert.True(preview.RequiresRestart);
    }

    [Fact]
    public async Task Game_bar_and_game_mode_do_not_need_a_restart()
    {
        var preview = await _runner.PreviewAsync([Tweak("game-mode.on"), Tweak("game-bar.background-recording-off")]);

        Assert.False(preview.RequiresRestart);
    }

    // ---- apply ----

    [Fact]
    public async Task Applying_writes_the_values_and_logs_one_record_each()
    {
        _registry.With(Ref("game-bar.background-recording-off", 0), RegistryValue.Dword(1));
        var log = NewLog();
        var run = log.StartRun();

        var report = await _runner.ApplyAsync([Tweak("game-bar.background-recording-off")], run);

        Assert.True(report.AllSucceeded);
        Assert.Equal(4, log.ReadRun(run.RunId).Records.Count);
        Assert.Equal(RegistryValue.Dword(0), _registry.GetValue(Ref("game-bar.background-recording-off", 0)));
    }

    // ---- Game Bar capture: the two levers added for O6 ----

    [Fact]
    public void Game_bar_capture_covers_both_the_background_and_the_manual_lever()
    {
        var names = _catalog.Find("game-bar.background-recording-off")!.Values
            .Select(value => value.Name)
            .ToList();

        Assert.Contains("GameDVR_Enabled", names);
        Assert.Contains("HistoricalCaptureEnabled", names);
        Assert.Contains("AppCaptureEnabled", names);
        Assert.Contains("AllowGameDVR", names);
    }

    [Fact]
    public void The_capture_values_live_under_the_documented_game_dvr_key()
    {
        var entry = _catalog.Find("game-bar.background-recording-off")!;

        foreach (var name in (string[])["HistoricalCaptureEnabled", "AppCaptureEnabled"])
        {
            var value = entry.Values.Single(candidate => candidate.Name == name);
            Assert.Equal(RegistryRoot.CurrentUser, value.ToRef().Root);
            Assert.Equal(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", value.ToRef().KeyPath);
            Assert.Equal(RegistryValueType.Dword, value.Type);
            Assert.Equal("0", value.Data);
        }
    }

    [Fact]
    public void The_name_no_longer_claims_to_be_background_only()
    {
        // AppCaptureEnabled also stops manual clip recording, so the old name would have
        // undersold what the tweak does. Decision 30.
        Assert.DoesNotContain(
            "background",
            _catalog.Find("game-bar.background-recording-off")!.NameEn,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Undo_restores_every_capture_value_the_tweak_touched()
    {
        var entry = _catalog.Find("game-bar.background-recording-off")!;
        // A PC with capture on and one value never set at all.
        _registry.With(entry.Values[0].ToRef(), RegistryValue.Dword(1));
        _registry.With(entry.Values[1].ToRef(), RegistryValue.Dword(1));
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("game-bar.background-recording-off")], log.StartRun());

        var undo = await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(RegistryValue.Dword(1), _registry.GetValue(entry.Values[0].ToRef()));
        Assert.Equal(RegistryValue.Dword(1), _registry.GetValue(entry.Values[1].ToRef()));
        Assert.Null(_registry.GetValue(entry.Values[2].ToRef()));
        Assert.Null(_registry.GetValue(entry.Values[3].ToRef()));
    }

    [Fact]
    public async Task The_record_keeps_the_value_type_so_undo_can_restore_it()
    {
        _registry.With(Ref("visual-effects.performance", 2), RegistryValue.Text("1"));
        var log = NewLog();
        var run = log.StartRun();

        await _runner.ApplyAsync([Tweak("visual-effects.performance")], run);

        var record = log.ReadRun(run.RunId).Records.Single(r => r.Target.EndsWith("MinAnimate", StringComparison.Ordinal));
        Assert.Equal("String:1", record.OldValue);
        Assert.Equal("String:0", record.NewValue);
    }

    [Fact]
    public async Task A_value_that_will_not_write_is_reported_and_the_rest_still_apply()
    {
        _registry.FailingTargets.Add(Ref("visual-effects.performance", 1).ToString());
        var log = NewLog();
        var run = log.StartRun();

        var report = await _runner.ApplyAsync([Tweak("visual-effects.performance")], run);

        Assert.Single(report.AllFailures);
        Assert.Equal(2, report.AllApplied.Count);
    }

    // ---- undo ----

    [Fact]
    public async Task Undo_restores_the_exact_previous_values()
    {
        _registry.With(Ref("visual-effects.performance", 0), RegistryValue.Dword(3));
        _registry.With(Ref("visual-effects.performance", 1), RegistryValue.Dword(1));
        _registry.With(Ref("visual-effects.performance", 2), RegistryValue.Text("1"));
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("visual-effects.performance")], log.StartRun());

        var undo = await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(RegistryValue.Dword(3), _registry.GetValue(Ref("visual-effects.performance", 0)));
        Assert.Equal(RegistryValue.Dword(1), _registry.GetValue(Ref("visual-effects.performance", 1)));
        Assert.Equal(RegistryValue.Text("1"), _registry.GetValue(Ref("visual-effects.performance", 2)));
    }

    [Fact]
    public async Task Undo_deletes_a_value_the_tweak_created_rather_than_leaving_a_zero_behind()
    {
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("game-mode.on")], log.StartRun());
        Assert.NotNull(_registry.GetValue(Ref("game-mode.on", 0)));

        await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.Null(_registry.GetValue(Ref("game-mode.on", 0)));
    }

    [Fact]
    public async Task Applying_gaming_then_work_visual_effects_still_undoes_to_the_users_own_values()
    {
        _registry.With(Ref("visual-effects.performance", 0), RegistryValue.Dword(3));
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("visual-effects.performance")], log.StartRun());
        await _runner.ApplyAsync([Tweak("visual-effects.appearance")], log.StartRun());

        await new UndoEngine(log, [new RegistryUndoHandler(_registry)]).UndoAllAsync();

        Assert.Equal(RegistryValue.Dword(3), _registry.GetValue(Ref("visual-effects.performance", 0)));
    }
}
