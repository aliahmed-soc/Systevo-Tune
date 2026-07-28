using System.Collections.ObjectModel;
using SystevoTune.App.Localization;
using SystevoTune.Engine.Safety;

namespace SystevoTune.App.ViewModels;

/// <summary>One change record, as a row.</summary>
public sealed class LogRecordRow(ChangeRecord record)
{
    /// <summary>Record id.</summary>
    public string Id { get; } = record.Id;

    /// <summary>When it was written.</summary>
    public DateTime Time { get; } = record.Time;

    /// <summary>Which module made it.</summary>
    public string Module { get; } = record.Module;

    /// <summary>What was done.</summary>
    public string Action { get; } = record.Action;

    /// <summary>What was changed.</summary>
    public string Target { get; } = record.Target;

    /// <summary>The value before.</summary>
    public string? OldValue { get; } = record.OldValue;

    /// <summary>The value after.</summary>
    public string? NewValue { get; } = record.NewValue;

    /// <summary>Whether it has been put back.</summary>
    public bool Undone { get; } = record.Undone;

    /// <summary>Whether it could ever be put back.</summary>
    public bool Undoable { get; } = record.Undoable;

    /// <summary>Which of the three states this record is in, for the badge.</summary>
    public string StateKey => !Undoable ? "Logs_Permanent" : Undone ? "Logs_Undone" : "Logs_Pending";
}

/// <summary>One logged run, expandable to its records.</summary>
public sealed class LogRunRow : ObservableObject
{
    private bool _isExpanded;

    internal LogRunRow(RunLog run, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RunId = run.RunId;
        ProfileId = run.ProfileId;
        SkippedLineCount = run.SkippedLineCount;
        Records = run.Changes.Select(record => new LogRecordRow(record)).ToList();
        PendingCount = run.Changes.Count(record => !record.Undone && record.Undoable);
        StartedAt = run.Records.Count > 0 ? run.Records[0].Time : default;

        // Both are templates with placeholders, so they are filled in here rather than bound
        // straight to the resource — which would render the braces.
        Summary = localizer.Format("Logs_RunSummary", Records.Count, PendingCount);
        TornLinesWarning = localizer.Format("Logs_TornLines", SkippedLineCount);
    }

    /// <summary>Change and pending counts, ready to display.</summary>
    public string Summary { get; }

    /// <summary>The torn-line warning, ready to display. Only shown when <see cref="HasTornLines"/>.</summary>
    public string TornLinesWarning { get; }

    /// <summary>Run id, which is also its file name.</summary>
    public string RunId { get; }

    /// <summary>The profile this run applied, or <c>null</c> for a one-off run.</summary>
    public string? ProfileId { get; }

    /// <summary>Changes in the run, metadata excluded.</summary>
    public IReadOnlyList<LogRecordRow> Records { get; }

    /// <summary>How many changes are still applied.</summary>
    public int PendingCount { get; }

    /// <summary>When the run started.</summary>
    public DateTime StartedAt { get; }

    /// <summary>
    /// Unreadable lines. Normally zero — anything else is the signature of a run that was killed
    /// mid-change, and worth showing rather than hiding.
    /// </summary>
    public int SkippedLineCount { get; }

    /// <summary>Whether the run has a torn line worth flagging.</summary>
    public bool HasTornLines => SkippedLineCount > 0;

    /// <summary>Whether the record list is showing.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }
}

/// <summary>
/// B3. A read-only window onto the JSON files doc 05 writes.
/// </summary>
/// <remarks>
/// Reads only. There is no undo button here on purpose: undo belongs on the results screen where
/// the consequences are explained, not next to a list of raw records where it would be one
/// mis-click from a surprise.
/// </remarks>
public sealed class LogViewerViewModel : ObservableObject
{
    private readonly ChangeLog _log;
    private bool _isBusy;
    private string? _error;
    private bool _hasLoaded;

    private readonly ILocalizer _localizer;

    /// <param name="log">The change log to read. Injectable, so tests use a temp folder.</param>
    /// <param name="localizer">Needed to fill in the run summary and torn-line templates.</param>
    public LogViewerViewModel(ChangeLog log, ILocalizer localizer)
    {
        _log = log;
        _localizer = localizer;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    /// <summary>Runs, newest first.</summary>
    public ObservableCollection<LogRunRow> Runs { get; } = [];

    /// <summary>Re-reads the log folder.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Where the logs live, shown so the user can open the folder themselves.</summary>
    public string LogFolder => _log.DirectoryPath;

    /// <summary>Whether a read is in flight.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Why the logs could not be read, or <c>null</c>.</summary>
    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>No runs on disk — different from not having looked yet.</summary>
    public bool IsEmpty => _hasLoaded && Runs.Count == 0;

    /// <summary>Total changes across every run.</summary>
    public int TotalChanges => Runs.Sum(run => run.Records.Count);

    /// <summary>Reads the log folder. A folder that cannot be read is reported, not thrown.</summary>
    public Task RefreshAsync()
    {
        IsBusy = true;
        Error = null;

        try
        {
            Runs.Clear();

            foreach (var run in _log.ReadAllRuns())
            {
                Runs.Add(new LogRunRow(run, _localizer));
            }

            _hasLoaded = true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Raise(nameof(IsEmpty));
            Raise(nameof(TotalChanges));
        }

        return Task.CompletedTask;
    }
}
