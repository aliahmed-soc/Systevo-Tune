using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tests.Fakes;
using SystevoTune.Engine.Tests.Safety;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;

namespace SystevoTune.Engine.Tests.Tweaks;

public class PowerPlanTweakTests : IDisposable
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid High = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private readonly TempLogDirectory _directory = new();
    private readonly FakePowerPlanService _powerPlans = new();
    private readonly FakeBatteryStatus _battery = new();
    private readonly PowerPlanCatalog _catalog = PowerPlanCatalog.Load();
    private readonly TweakRunner _runner = new();

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog()
        => new(_directory.Path, new FixedClock(new DateTimeOffset(2026, 7, 26, 14, 0, 0, TimeSpan.Zero)));

    private PowerPlanTweak Tweak(string planId) => new(_powerPlans, _catalog, _battery, planId);

    // ---- the catalogue ----

    [Fact]
    public void The_shipped_catalogue_has_the_plans_doc_3_4_needs()
    {
        Assert.Equal(Balanced, _catalog.Find("balanced")!.Guid);
        Assert.Equal(High, _catalog.Find("high-performance")!.Guid);
        Assert.Equal(Ultimate, _catalog.Find("ultimate-performance")!.Guid);
    }

    [Fact]
    public void The_catalogue_matches_the_worked_example_in_doc_5_2()
    {
        // Doc 5.2's record shows 381b4222 -> 8c5e7fda, i.e. Balanced -> High performance.
        Assert.Equal("balanced", _catalog.Find(Balanced)!.Id);
        Assert.Equal("high-performance", _catalog.Find(High)!.Id);
    }

    [Fact]
    public void Every_catalogue_entry_has_an_arabic_name()
        => Assert.All(_catalog.Plans, plan => Assert.False(string.IsNullOrWhiteSpace(plan.NameAr)));

    [Fact]
    public void A_duplicate_plan_id_is_refused()
    {
        const string json = """
            {"plans":[{"id":"a","guid":"381b4222-f694-41f0-9685-ff5bb260df2e","nameEn":"A","nameAr":"أ"},
                      {"id":"a","guid":"8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c","nameEn":"B","nameAr":"ب"}]}
            """;

        Assert.Throws<InvalidOperationException>(() => PowerPlanCatalog.Parse(json));
    }

    // ---- preview ----

    [Fact]
    public async Task Switching_to_high_reports_the_old_and_new_guid()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");

        var plan = await Tweak("high-performance").PlanAsync(CancellationToken.None);

        var change = Assert.Single(plan.Changes);
        Assert.Equal("ActivePowerScheme", change.Target);
        Assert.Equal(Balanced.ToString("D"), change.OldValue);
        Assert.Equal(High.ToString("D"), change.NewValue);
    }

    [Fact]
    public async Task Preview_switches_nothing()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");

        await _runner.PreviewAsync([Tweak("high-performance")]);

        Assert.Empty(_powerPlans.Activated);
        Assert.Equal(Balanced, _powerPlans.Active);
    }

    [Fact]
    public async Task The_description_names_both_plans_rather_than_showing_guids()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");

        var plan = await Tweak("high-performance").PlanAsync(CancellationToken.None);

        Assert.Equal("Power plan: Balanced to High performance.", Assert.Single(plan.Changes).Description);
    }

    [Fact]
    public async Task Already_being_on_the_target_plan_is_nothing_to_do()
    {
        _powerPlans.With(High, "High performance", isActive: true);

        var plan = await Tweak("high-performance").PlanAsync(CancellationToken.None);

        Assert.Equal(TweakStatus.AlreadyApplied, plan.Status);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public async Task A_plan_this_pc_does_not_have_is_not_applicable_rather_than_an_error()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");

        var plan = await Tweak("ultimate-performance").PlanAsync(CancellationToken.None);

        Assert.Equal(TweakStatus.NotApplicable, plan.Status);
        Assert.Contains("could not be found", plan.Message!, StringComparison.Ordinal);
        // The message names what the PC does have, so the user is not left guessing.
        Assert.Contains("High performance", plan.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_id_that_is_not_in_the_whitelist_is_a_build_error()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Tweak("turbo-mode").PlanAsync(CancellationToken.None));
    }

    // ---- battery warning, doc 3.4 ----

    [Fact]
    public async Task Switching_to_high_on_battery_warns_but_still_offers_the_change()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
        _battery.Current = BatteryState.OnBattery;

        var plan = await Tweak("high-performance").PlanAsync(CancellationToken.None);

        Assert.Equal(TweakStatus.Ready, plan.Status);
        Assert.Contains("running on battery", plan.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plugged_in_laptop_gets_no_battery_warning()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
        _battery.Current = BatteryState.PluggedIn;

        Assert.Null((await Tweak("high-performance").PlanAsync(CancellationToken.None)).Message);
    }

    [Fact]
    public async Task Switching_to_balanced_on_battery_needs_no_warning()
    {
        _powerPlans.With(Balanced, "Balanced").With(High, "High performance", isActive: true);
        _battery.Current = BatteryState.OnBattery;

        Assert.Null((await Tweak("balanced").PlanAsync(CancellationToken.None)).Message);
    }

    // ---- apply and undo ----

    [Fact]
    public async Task Applying_switches_the_plan_and_logs_it()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
        var log = NewLog();
        var run = log.StartRun();

        var report = await _runner.ApplyAsync([Tweak("high-performance")], run);

        Assert.True(report.AllSucceeded);
        Assert.Equal(High, _powerPlans.Active);
        var record = Assert.Single(log.ReadRun(run.RunId).Records);
        Assert.Equal("PowerPlan", record.Module);
        Assert.Equal("SetActivePlan", record.Action);
        Assert.True(record.Undoable);
    }

    [Fact]
    public async Task Undo_puts_the_previous_plan_back()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("high-performance")], log.StartRun());

        var undo = await new UndoEngine(log, [new PowerPlanUndoHandler(_powerPlans)]).UndoAllAsync();

        Assert.True(undo.AllSucceeded);
        Assert.Equal(Balanced, _powerPlans.Active);
    }

    [Fact]
    public async Task Undo_restores_the_previous_plan_not_the_windows_default()
    {
        // Doc 7.3: undo must restore what was there, which here is Power saver, not Balanced.
        var saver = _catalog.Find("power-saver")!.Guid;
        _powerPlans.With(saver, "Power saver", isActive: true).With(Balanced, "Balanced").With(High, "High performance");
        var log = NewLog();
        await _runner.ApplyAsync([Tweak("high-performance")], log.StartRun());

        await new UndoEngine(log, [new PowerPlanUndoHandler(_powerPlans)]).UndoAllAsync();

        Assert.Equal(saver, _powerPlans.Active);
    }

    [Fact]
    public async Task A_switch_that_fails_is_reported_and_leaves_its_record()
    {
        _powerPlans.With(Balanced, "Balanced", isActive: true).With(High, "High performance");
        _powerPlans.SetFailure = new InvalidOperationException("powercfg exit code 1");
        var log = NewLog();
        var run = log.StartRun();

        var report = await _runner.ApplyAsync([Tweak("high-performance")], run);

        Assert.Contains("powercfg", Assert.Single(report.AllFailures).Reason, StringComparison.Ordinal);
        Assert.Single(log.ReadRun(run.RunId).Records);
    }

    [Fact]
    public async Task Undo_says_so_plainly_when_the_old_plan_was_never_recorded()
    {
        var log = NewLog();
        var run = log.StartRun();
        run.RecordChange("PowerPlan", "SetActivePlan", "ActivePowerScheme", null, High.ToString("D"));

        var undo = await new UndoEngine(log, [new PowerPlanUndoHandler(_powerPlans)]).UndoAllAsync();

        Assert.Contains("Power Options", Assert.Single(undo.Failures).Reason, StringComparison.Ordinal);
    }
}
