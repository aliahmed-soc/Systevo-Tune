using System.Text.Json;
using SystevoTune.Engine.Safety;

namespace SystevoTune.Engine.Tests.Safety;

public class ChangeLogTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 7, 26, 14, 3, 22, TimeSpan.Zero);

    private readonly TempLogDirectory _directory = new();
    private readonly FixedClock _clock = new(Noon);

    public void Dispose() => _directory.Dispose();

    private ChangeLog NewLog() => new(_directory.Path, _clock);

    [Fact]
    public void Default_directory_is_under_program_data()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SystevoTune",
            "logs");

        Assert.Equal(expected, ChangeLog.DefaultDirectory);
    }

    [Fact]
    public void Record_is_on_disk_as_soon_as_it_is_recorded()
    {
        var run = NewLog().StartRun();

        run.RecordChange("PowerPlan", "SetActivePlan", "ActivePowerScheme", "old-guid", "new-guid");

        var lines = File.ReadAllLines(run.FilePath);
        Assert.Single(lines);
        Assert.Contains("ActivePowerScheme", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Record_on_disk_uses_the_documented_field_names_and_time_format()
    {
        var run = NewLog().StartRun();
        run.RecordChange("PowerPlan", "SetActivePlan", "ActivePowerScheme", "old-guid", "new-guid");

        using var document = JsonDocument.Parse(File.ReadAllLines(run.FilePath)[0]);
        var root = document.RootElement;

        Assert.Equal("2026-07-26-001", root.GetProperty("id").GetString());
        Assert.Equal("2026-07-26T14:03:22", root.GetProperty("time").GetString());
        Assert.Equal("PowerPlan", root.GetProperty("module").GetString());
        Assert.Equal("SetActivePlan", root.GetProperty("action").GetString());
        Assert.Equal("ActivePowerScheme", root.GetProperty("target").GetString());
        Assert.Equal("old-guid", root.GetProperty("oldValue").GetString());
        Assert.Equal("new-guid", root.GetProperty("newValue").GetString());
        Assert.False(root.GetProperty("undone").GetBoolean());
    }

    [Fact]
    public void Records_read_back_exactly_as_they_were_written()
    {
        var log = NewLog();
        var run = log.StartRun();
        var written = run.RecordChange("Cleanup", "DeleteFile", @"C:\Windows\Temp\a.tmp", "4096", null);

        var read = log.ReadRun(run.RunId);

        Assert.Equal(0, read.SkippedLineCount);
        Assert.Equal(written, Assert.Single(read.Records));
    }

    [Fact]
    public void Records_read_back_in_the_order_they_were_written()
    {
        var log = NewLog();
        var run = log.StartRun();
        run.RecordChange("Cleanup", "DeleteFile", "first", "1", null);
        run.RecordChange("Cleanup", "DeleteFile", "second", "2", null);
        run.RecordChange("Cleanup", "DeleteFile", "third", "3", null);

        var read = log.ReadRun(run.RunId);

        Assert.Equal(["first", "second", "third"], read.Records.Select(r => r.Target));
        Assert.Equal(["2026-07-26-001", "2026-07-26-002", "2026-07-26-003"], read.Records.Select(r => r.Id));
    }

    [Fact]
    public void A_null_old_value_survives_the_round_trip()
    {
        var log = NewLog();
        var run = log.StartRun();
        run.RecordChange("Privacy", "SetValue", "TelemetryLevel", null, "0");

        var record = Assert.Single(log.ReadRun(run.RunId).Records);

        Assert.Null(record.OldValue);
        Assert.Equal("0", record.NewValue);
    }

    [Fact]
    public void Each_run_gets_its_own_file()
    {
        var log = NewLog();

        var first = log.StartRun();
        var second = log.StartRun();

        Assert.NotEqual(first.RunId, second.RunId);
        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.Equal(2, log.ListRunIds().Count);
    }

    [Fact]
    public void Two_runs_in_the_same_second_do_not_overwrite_each_other()
    {
        var log = NewLog();

        var first = log.StartRun();
        first.RecordChange("Cleanup", "DeleteFile", "first", "1", null);
        var second = log.StartRun();
        second.RecordChange("Cleanup", "DeleteFile", "second", "2", null);

        Assert.Equal("first", Assert.Single(log.ReadRun(first.RunId).Records).Target);
        Assert.Equal("second", Assert.Single(log.ReadRun(second.RunId).Records).Target);
    }

    [Fact]
    public void A_second_run_on_the_same_day_continues_the_id_sequence()
    {
        var log = NewLog();
        var first = log.StartRun();
        first.RecordChange("Cleanup", "DeleteFile", "first", "1", null);
        first.RecordChange("Cleanup", "DeleteFile", "second", "2", null);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = log.StartRun();
        var third = second.RecordChange("Cleanup", "DeleteFile", "third", "3", null);

        Assert.Equal("2026-07-26-003", third.Id);
    }

    [Fact]
    public void A_run_on_a_new_day_restarts_the_id_sequence()
    {
        var log = NewLog();
        log.StartRun().RecordChange("Cleanup", "DeleteFile", "yesterday", "1", null);

        _clock.Advance(TimeSpan.FromDays(1));
        var today = log.StartRun().RecordChange("Cleanup", "DeleteFile", "today", "2", null);

        Assert.Equal("2026-07-27-001", today.Id);
    }

    [Fact]
    public void Run_ids_are_listed_newest_first()
    {
        var log = NewLog();
        var first = log.StartRun();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = log.StartRun();

        Assert.Equal([second.RunId, first.RunId], log.ListRunIds());
    }

    [Fact]
    public void An_empty_log_directory_lists_no_runs()
    {
        var log = new ChangeLog(Path.Combine(_directory.Path, "not-created-yet"), _clock);

        Assert.Empty(log.ListRunIds());
        Assert.Empty(log.ReadAllRuns());
    }

    [Fact]
    public void A_torn_last_line_is_skipped_and_counted_instead_of_throwing()
    {
        var log = NewLog();
        var run = log.StartRun();
        run.RecordChange("Cleanup", "DeleteFile", "complete", "1", null);
        File.AppendAllText(run.FilePath, "{\"id\":\"2026-07-26-002\",\"modu");

        var read = log.ReadRun(run.RunId);

        Assert.Equal("complete", Assert.Single(read.Records).Target);
        Assert.Equal(1, read.SkippedLineCount);
    }

    [Fact]
    public void Mark_undone_updates_only_the_named_record()
    {
        var log = NewLog();
        var run = log.StartRun();
        var first = run.RecordChange("Cleanup", "DeleteFile", "first", "1", null);
        run.RecordChange("Cleanup", "DeleteFile", "second", "2", null);

        var marked = log.MarkUndone(run.RunId, first.Id);

        Assert.True(marked);
        var records = log.ReadRun(run.RunId).Records;
        Assert.True(records[0].Undone);
        Assert.False(records[1].Undone);
    }

    [Fact]
    public void Mark_undone_reports_false_for_an_unknown_record()
    {
        var log = NewLog();
        var run = log.StartRun();
        run.RecordChange("Cleanup", "DeleteFile", "first", "1", null);

        Assert.False(log.MarkUndone(run.RunId, "2026-07-26-999"));
    }

    [Fact]
    public void Mark_undone_keeps_torn_lines_and_leaves_no_temp_file()
    {
        var log = NewLog();
        var run = log.StartRun();
        var record = run.RecordChange("Cleanup", "DeleteFile", "first", "1", null);
        File.AppendAllText(run.FilePath, "{\"id\":\"2026-07-26-002\",\"modu");

        log.MarkUndone(run.RunId, record.Id);

        var read = log.ReadRun(run.RunId);
        Assert.True(Assert.Single(read.Records).Undone);
        Assert.Equal(1, read.SkippedLineCount);
        Assert.Empty(Directory.EnumerateFiles(_directory.Path, "*.tmp"));
    }

    [Fact]
    public void Reading_an_unknown_run_throws()
        => Assert.Throws<FileNotFoundException>(() => NewLog().ReadRun("2026-01-01_00-00-00"));

    [Fact]
    public void A_run_id_that_is_a_path_is_rejected()
        => Assert.Throws<ArgumentException>(() => NewLog().ReadRun(@"..\..\escape"));

    [Fact]
    public void Recording_a_change_with_no_target_throws()
    {
        var run = NewLog().StartRun();

        Assert.Throws<ArgumentException>(() => run.RecordChange("Cleanup", "DeleteFile", "  ", "1", null));
    }
}
