using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tests.Fakes;
using SystevoTune.Engine.Tests.Safety;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;

namespace SystevoTune.Engine.Tests.Tweaks;

/// <summary>
/// O1: a PC may not have the scheme GUID we expect. Microsoft documents the three GUIDs as
/// personalities that every scheme "maps to", so an OEM image can ship its own. These cover the
/// machine shapes that would otherwise only show up on someone's laptop.
/// </summary>
public class PowerPlanResolutionTests : IDisposable
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid High = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static readonly Guid OemScheme = new("11111111-2222-3333-4444-555555555555");

    private readonly TempLogDirectory _directory = new();
    private readonly FakePowerPlanService _powerPlans = new();
    private readonly FakeBatteryStatus _battery = new();
    private readonly PowerPlanCatalog _catalog = PowerPlanCatalog.Load();
    private readonly TweakRunner _runner = new();

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog()
        => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero)));

    private PowerPlanTweak Tweak(params string[] preferred) => new(_powerPlans, _catalog, _battery, preferred);

    private UndoEngine Undo(ChangeLog log) => new(log, [new PowerPlanUndoHandler(_powerPlans)]);

    // ---- matching by GUID ----

    [Fact]
    public async Task A_stock_install_matches_on_the_documented_guid()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");

        await _runner.ApplyAsync([Tweak("high-performance")], NewLog().StartRun());

        Assert.Equal(High, _powerPlans.Active);
        Assert.Empty(_powerPlans.Created);
    }

    // ---- matching by name: the OEM case ----

    [Fact]
    public async Task An_oem_scheme_named_high_performance_is_matched_by_name()
    {
        // The GUID is the OEM's own, but the name is Windows'. Assuming the GUID would leave the
        // PC on Balanced while reporting success.
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(OemScheme, "High performance");

        await _runner.ApplyAsync([Tweak("high-performance")], NewLog().StartRun());

        Assert.Equal(OemScheme, _powerPlans.Active);
        Assert.Empty(_powerPlans.Created);
    }

    [Fact]
    public async Task Name_matching_ignores_case()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(OemScheme, "HIGH PERFORMANCE");

        await _runner.ApplyAsync([Tweak("high-performance")], NewLog().StartRun());

        Assert.Equal(OemScheme, _powerPlans.Active);
    }

    [Fact]
    public async Task An_unrelated_oem_scheme_is_never_mistaken_for_the_one_we_want()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(OemScheme, "Dell Optimized");

        var preview = await _runner.PreviewAsync([Tweak("high-performance")]);

        // No silent switch to something the profile never asked for. It plans a fresh scheme
        // instead, and says so.
        Assert.DoesNotContain(preview.AllChanges, change => change.NewValue == OemScheme.ToString("D"));
        Assert.Contains(preview.AllChanges, change => change.Description.Contains("create", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_plan_with_no_template_degrades_with_a_message_naming_what_the_pc_has()
    {
        // Balanced carries no template on purpose, so this is the pure degrade path.
        _powerPlans.With(OemScheme, "Dell Optimized", isActive: true);

        var preview = await _runner.PreviewAsync([Tweak("balanced")]);

        var message = Assert.Single(preview.Plans).Message!;
        Assert.Equal(TweakStatus.NotApplicable, preview.Plans[0].Status);
        Assert.Contains("Dell Optimized", message, StringComparison.Ordinal);
    }

    // ---- the overlay-only machine ----

    [Fact]
    public async Task An_overlay_only_machine_gets_high_performance_created_for_it()
    {
        // Windows 11 power-mode machines often list Balanced alone. Ultimate is not invented —
        // it falls through to creating High, which is the milder change (decision 31).
        _powerPlans.With(Balanced, "Balanced", isActive: true);
        _powerPlans.DuplicableTemplates.Add(High);
        var created = _catalog.Find("high-performance")!.CreateAs!.Value;

        var report = await _runner.ApplyAsync(
            [Tweak("ultimate-performance", "high-performance")], NewLog().StartRun());

        Assert.True(report.AllSucceeded);
        Assert.Equal([created], _powerPlans.Created);
        Assert.Equal(created, _powerPlans.Active);
    }

    [Fact]
    public async Task Ultimate_performance_is_never_invented_on_a_pc_that_lacks_it()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
        _powerPlans.DuplicableTemplates.Add(Ultimate);

        await _runner.ApplyAsync([Tweak("ultimate-performance", "high-performance")], NewLog().StartRun());

        Assert.Empty(_powerPlans.Created);
        Assert.Equal(High, _powerPlans.Active);
    }

    [Fact]
    public async Task A_machine_reporting_no_schemes_at_all_does_not_crash()
    {
        var preview = await _runner.PreviewAsync([Tweak("balanced")]);

        Assert.Contains("no power schemes at all", Assert.Single(preview.Plans).Message!, StringComparison.Ordinal);
    }

    // ---- creating a missing scheme ----

    [Fact]
    public async Task A_missing_scheme_is_created_from_its_template_then_activated()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true);
        _powerPlans.DuplicableTemplates.Add(High);
        var created = _catalog.Find("high-performance")!.CreateAs!.Value;

        var report = await _runner.ApplyAsync([Tweak("high-performance")], NewLog().StartRun());

        Assert.True(report.AllSucceeded);
        Assert.Equal([created], _powerPlans.Created);
        Assert.Equal(created, _powerPlans.Active);
    }

    [Fact]
    public async Task Creating_a_scheme_is_logged_before_it_happens()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true);
        _powerPlans.DuplicableTemplates.Add(High);
        var log = NewLog();
        var run = log.StartRun();

        await _runner.ApplyAsync([Tweak("high-performance")], run);

        var records = log.ReadRun(run.RunId).Records;
        Assert.Equal("CreateScheme", records[0].Action);
        Assert.Equal("SetActivePlan", records[1].Action);
        Assert.Null(records[0].OldValue);
    }

    [Fact]
    public async Task Undo_deletes_a_created_scheme_and_restores_the_old_one()
    {
        // Doc 07.2 diffs the VM against its snapshot, so an invented scheme is a bug unless undo
        // removes it.
        _powerPlans.With(Balanced, "Balanced", isActive: true);
        _powerPlans.DuplicableTemplates.Add(High);
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("high-performance")], log.StartRun());
        var created = _catalog.Find("high-performance")!.CreateAs!.Value;

        var undo = await Undo(log).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(Balanced, _powerPlans.Active);
        Assert.Equal([created], _powerPlans.Deleted);
    }

    [Fact]
    public async Task Undo_switches_back_before_deleting_so_windows_does_not_refuse()
    {
        // Deleting the active scheme fails on a real PC. Newest-first ordering is what avoids it,
        // and the fake throws if we get it wrong.
        _powerPlans.With(Balanced, "Balanced", isActive: true);
        _powerPlans.DuplicableTemplates.Add(High);
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("high-performance")], log.StartRun());

        var undo = await Undo(log).UndoAllAsync();

        Assert.Empty(undo.Failures);
    }

    [Fact]
    public async Task A_second_run_reuses_the_scheme_it_created_rather_than_making_another()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true);
        _powerPlans.DuplicableTemplates.Add(High);
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("high-performance")], log.StartRun());

        var second = await _runner.PreviewAsync([Tweak("high-performance")]);

        Assert.Equal(TweakStatus.AlreadyApplied, Assert.Single(second.Plans).Status);
        Assert.Single(_powerPlans.Created);
    }

    [Fact]
    public async Task A_template_windows_will_not_copy_fails_loudly_rather_than_silently()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true);
        // No duplicable templates: powercfg refuses, as it would on a PC without the template.

        var report = await _runner.ApplyAsync([Tweak("high-performance")], NewLog().StartRun());

        Assert.Contains("would not copy", report.AllFailures[0].Reason, StringComparison.Ordinal);
        Assert.Equal(Balanced, _powerPlans.Active);
    }

    [Fact]
    public async Task A_failed_creation_does_not_leave_the_pc_on_a_scheme_that_does_not_exist()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true);

        var report = await _runner.ApplyAsync([Tweak("high-performance")], NewLog().StartRun());

        // Both steps fail — the create, then the switch to something that was never made. What
        // matters is that the PC is untouched rather than pointed at a phantom scheme.
        Assert.False(report.AllSucceeded);
        Assert.Equal(Balanced, _powerPlans.Active);
        Assert.Empty(_powerPlans.Activated);
    }
}
