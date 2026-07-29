using System.IO;
using SystevoTune.App.Localization;
using SystevoTune.App.ViewModels;
using SystevoTune.Engine.Safety;
using SystevoTune.TestSupport;

namespace SystevoTune.App.Tests;

/// <summary>B3 (log viewer) and B4 (settings).</summary>
public class LogAndSettingsTests : IDisposable
{
    private readonly TempLogDirectory _directory = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.Zero));
    private readonly ChangeLog _log;

    public LogAndSettingsTests() => _log = new ChangeLog(_directory.Path, _clock);

    public void Dispose() => _directory.Dispose();

    private static ILocalizer NewLocalizer() => new Localizer(Localizer.LoadEmbeddedPacks());

    // ================= Log viewer =================

    [Fact]
    public async Task An_empty_log_folder_says_so_rather_than_showing_a_blank_list()
    {
        var model = new LogViewerViewModel(_log, NewLocalizer());

        await model.RefreshAsync();

        Assert.True(model.IsEmpty);
        Assert.Empty(model.Runs);
    }

    [Fact]
    public void Not_having_looked_yet_is_not_the_same_as_empty()
        => Assert.False(new LogViewerViewModel(_log, NewLocalizer()).IsEmpty);

    [Fact]
    public async Task Runs_are_listed_newest_first_with_their_records()
    {
        var first = _log.StartRun();
        first.RecordChange("Registry", "SetValue", "HKCU\\A::B", "Dword:0", "Dword:1");
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = _log.StartRun();
        second.RecordChange("PowerPlan", "SetActivePlan", "ActivePowerScheme", "g1", "g2");

        var model = new LogViewerViewModel(_log, NewLocalizer());
        await model.RefreshAsync();

        Assert.Equal(2, model.Runs.Count);
        Assert.Equal(second.RunId, model.Runs[0].RunId);
        Assert.Equal("ActivePowerScheme", model.Runs[0].Records[0].Target);
    }

    [Fact]
    public async Task A_record_shows_its_old_and_new_value()
    {
        var run = _log.StartRun();
        run.RecordChange("Registry", "SetValue", "HKCU\\A::B", "Dword:3", "Dword:2");
        var model = new LogViewerViewModel(_log, NewLocalizer());

        await model.RefreshAsync();

        var record = model.Runs[0].Records[0];
        Assert.Equal("Dword:3", record.OldValue);
        Assert.Equal("Dword:2", record.NewValue);
    }

    [Fact]
    public async Task The_profile_marker_is_shown_as_the_run_profile_not_as_a_change()
    {
        var run = _log.StartRun();
        run.RecordProfile("gaming");
        run.RecordChange("Registry", "SetValue", "HKCU\\A::B", null, "Dword:1");
        var model = new LogViewerViewModel(_log, NewLocalizer());

        await model.RefreshAsync();

        Assert.Equal("gaming", model.Runs[0].ProfileId);
        Assert.Single(model.Runs[0].Records);
    }

    [Fact]
    public async Task Each_record_is_badged_as_pending_undone_or_permanent()
    {
        var run = _log.StartRun();
        var pending = run.RecordChange("Registry", "SetValue", "a", "1", "2");
        run.RecordChange("Registry", "SetValue", "b", "1", "2");
        run.RecordChange("Cleanup", "DeleteGroupContents", "temp-files", "files=1;bytes=1", "files=0;bytes=0", undoable: false);
        _log.MarkUndone(run.RunId, pending.Id);

        var model = new LogViewerViewModel(_log, NewLocalizer());
        await model.RefreshAsync();

        var keys = model.Runs[0].Records.Select(record => record.StateKey).ToList();
        Assert.Equal(["Logs_Undone", "Logs_Pending", "Logs_Permanent"], keys);
    }

    [Fact]
    public async Task The_pending_count_ignores_permanent_and_undone_records()
    {
        var run = _log.StartRun();
        run.RecordChange("Registry", "SetValue", "a", "1", "2");
        run.RecordChange("Cleanup", "DeleteGroupContents", "temp-files", "x", "y", undoable: false);
        var model = new LogViewerViewModel(_log, NewLocalizer());

        await model.RefreshAsync();

        Assert.Equal(1, model.Runs[0].PendingCount);
    }

    [Fact]
    public async Task A_torn_line_is_flagged_because_it_means_a_run_was_killed_mid_change()
    {
        var run = _log.StartRun();
        run.RecordChange("Registry", "SetValue", "a", "1", "2");
        File.AppendAllText(run.FilePath, "{\"id\":\"2026-07-27-002\",\"modu");

        var model = new LogViewerViewModel(_log, NewLocalizer());
        await model.RefreshAsync();

        Assert.True(model.Runs[0].HasTornLines);
        Assert.Equal(1, model.Runs[0].SkippedLineCount);
    }

    [Fact]
    public async Task A_log_folder_that_does_not_exist_yet_reads_as_empty_rather_than_an_error()
    {
        // The state on a PC where nothing has been applied. ChangeLog is defensive enough that
        // this is genuinely not an error — the view model's catch is there for a torn folder,
        // which no test can arrange without breaking the file system underneath it.
        var fresh = new LogViewerViewModel(
            new ChangeLog(Path.Combine(_directory.Path, "never-created"), _clock), NewLocalizer());

        await fresh.RefreshAsync();

        Assert.Null(fresh.Error);
        Assert.True(fresh.IsEmpty);
    }

    [Fact]
    public void The_log_folder_is_shown_so_the_user_can_open_it()
        => Assert.Equal(_directory.Path, new LogViewerViewModel(_log, NewLocalizer()).LogFolder);

    // ================= Settings =================

    [Fact]
    public void Restore_points_default_to_on()
        => Assert.True(new SettingsViewModel(NewLocalizer(), _log).CreateRestorePoint);

    [Fact]
    public void Turning_restore_points_off_shows_a_warning()
    {
        var model = new SettingsViewModel(NewLocalizer(), _log);

        Assert.False(model.ShowRestorePointWarning);

        model.CreateRestorePoint = false;

        Assert.True(model.ShowRestorePointWarning);
    }

    [Fact]
    public void The_restore_point_choice_is_written_into_the_run_log()
    {
        // Months later, a log has to say whether the safety net was switched on for that run.
        var model = new SettingsViewModel(NewLocalizer(), _log) { CreateRestorePoint = false };
        var run = _log.StartRun();

        model.RecordInto(run);

        var record = Assert.Single(_log.ReadRun(run.RunId).Records);
        Assert.Equal("CreateRestorePointBeforeApply", record.Target);
        Assert.Equal("off", record.NewValue);
    }

    [Fact]
    public async Task The_setting_record_is_metadata_so_undo_ignores_it()
    {
        var model = new SettingsViewModel(NewLocalizer(), _log);
        var run = _log.StartRun();
        model.RecordInto(run);

        var undo = await new UndoEngine(_log, []).UndoAllAsync();

        Assert.Equal(0, undo.AttemptedCount);
        Assert.Empty(undo.Permanent);
    }

    [Fact]
    public void Changing_the_language_from_settings_switches_the_whole_app()
    {
        var localizer = NewLocalizer();
        var model = new SettingsViewModel(localizer, _log);

        model.CurrentLanguage = Language.Arabic;

        Assert.Equal("ar", localizer.Current.Code);
    }

    [Fact]
    public async Task The_run_summary_and_torn_line_warning_are_filled_in_not_left_as_templates()
    {
        // Bound straight to the resource these rendered "{0} change(s), {1} still to undo".
        var run = _log.StartRun();
        run.RecordChange("Registry", "SetValue", "a", "1", "2");
        File.AppendAllText(run.FilePath, "{\"id\":\"x\",\"modu");

        var model = new LogViewerViewModel(_log, NewLocalizer());
        await model.RefreshAsync();

        var row = model.Runs[0];
        Assert.DoesNotContain("{0}", row.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("{1}", row.Summary, StringComparison.Ordinal);
        Assert.Contains("1", row.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", row.TornLinesWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void The_language_toggle_swaps_languages_and_relabels_itself()
    {
        // The switcher was a ComboBox until the first VM run, where it rendered as an empty box
        // and made Arabic unreachable. A button is labelled with where it *goes*, not where it is,
        // so the label has to follow the switch — otherwise it lies after a single press and there
        // is no way back to English.
        var localizer = NewLocalizer();
        var model = new SettingsViewModel(localizer, _log);

        Assert.Equal("en", model.CurrentLanguage.Code);
        Assert.Equal("ar", model.OtherLanguage.Code);

        model.ToggleLanguageCommand.Execute(null);

        Assert.Equal("ar", localizer.Current.Code);
        Assert.Equal("ar", model.CurrentLanguage.Code);
        Assert.Equal("en", model.OtherLanguage.Code);

        model.ToggleLanguageCommand.Execute(null);

        Assert.Equal("en", localizer.Current.Code);
        Assert.Equal("ar", model.OtherLanguage.Code);
    }

    [Fact]
    public void Settings_shows_where_the_logs_are_and_which_engine_built_it()
    {
        var model = new SettingsViewModel(NewLocalizer(), _log);

        Assert.Equal(_directory.Path, model.LogFolder);
        Assert.False(string.IsNullOrWhiteSpace(model.EngineVersion));
    }
}
