using System.Collections.ObjectModel;
using SystevoTune.App.Localization;
using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.App.ViewModels;

/// <summary>One thing undo could not put back.</summary>
public sealed class UndoFailureRow(string target, string reason)
{
    /// <summary>What could not be restored.</summary>
    public string Target { get; } = target;

    /// <summary>Why, in the engine's own words.</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// Screen 4. The summary, and the big Undo All button doc 06 asks for.
/// </summary>
/// <remarks>
/// The undo result is reported in three parts on purpose, because they mean different things:
/// what went back, what failed and can be retried, and what was never undoable at all. Collapsing
/// them into one number would let a cleanup deletion read as a failure, or a real failure hide
/// among the permanent ones.
/// </remarks>
public sealed class ResultsViewModel : ObservableObject
{
    private readonly UndoEngine _undo;
    private readonly ReapplyService _reapply;

    private ApplyReport? _applyReport;
    private bool _isUndoing;
    private string? _error;
    private UndoReport? _undoResult;
    private long _freedBytes;
    private int _lockedFiles;

    private readonly ILocalizer _localizer;

    /// <param name="undo">The undo engine.</param>
    /// <param name="reapply">Finds the last applied profile.</param>
    /// <param name="localizer">Needed to name the profile on the re-apply button.</param>
    public ResultsViewModel(UndoEngine undo, ReapplyService reapply, ILocalizer localizer)
    {
        _undo = undo;
        _reapply = reapply;
        _localizer = localizer;

        UndoAllCommand = new AsyncRelayCommand(UndoAllAsync, () => !IsUndoing);
    }

    /// <summary>Puts everything back, newest first.</summary>
    public AsyncRelayCommand UndoAllCommand { get; }

    /// <summary>Changes that could not be put back. Retryable.</summary>
    public ObservableCollection<UndoFailureRow> UndoFailures { get; } = [];

    /// <summary>Changes that were never undoable — deleted files.</summary>
    public ObservableCollection<string> PermanentChanges { get; } = [];

    /// <summary>How many changes were applied.</summary>
    public int AppliedCount => _applyReport?.AllApplied.Count ?? 0;

    /// <summary>How many changes failed during apply.</summary>
    public int FailedCount => _applyReport?.AllFailures.Count ?? 0;

    /// <summary>Whether the applied run needs a restart.</summary>
    public bool RequiresRestart => _applyReport?.RequiresRestart ?? false;

    /// <summary>Nothing was applied, so there is nothing to celebrate or undo.</summary>
    public bool NothingApplied => AppliedCount == 0;

    /// <summary>Disk space freed by cleanup, formatted.</summary>
    public string FreedSpace => CleanupScanReport.Humanise(_freedBytes);

    /// <summary>Raw freed bytes.</summary>
    public long FreedBytes => _freedBytes;

    /// <summary>Files cleanup left alone because they were in use.</summary>
    public int LockedFiles => _lockedFiles;

    /// <summary>Whether an undo pass is in flight.</summary>
    public bool IsUndoing
    {
        get => _isUndoing;
        private set
        {
            if (Set(ref _isUndoing, value))
            {
                UndoAllCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Why the undo could not run at all, or <c>null</c>.</summary>
    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>The undo result. <c>null</c> until Undo All has run.</summary>
    public UndoReport? UndoResult
    {
        get => _undoResult;
        private set
        {
            if (Set(ref _undoResult, value))
            {
                Raise(nameof(UndoneCount));
                Raise(nameof(HasUndone));
                Raise(nameof(UndoWasPartial));
                Raise(nameof(UndoFoundNothing));
            }
        }
    }

    /// <summary>How many changes went back.</summary>
    public int UndoneCount => UndoResult?.Undone.Count ?? 0;

    /// <summary>Whether an undo pass has completed.</summary>
    public bool HasUndone => UndoResult is not null;

    /// <summary>Some went back and some did not — the case the user most needs told about.</summary>
    public bool UndoWasPartial => UndoResult is { } report && report.Undone.Count > 0 && report.Failures.Count > 0;

    /// <summary>Undo ran but there was nothing left to put back.</summary>
    public bool UndoFoundNothing => UndoResult is { } report && report.AttemptedCount == 0;

    /// <summary>The profile that could be re-applied, or <c>null</c>.</summary>
    public ReapplyTarget? LastProfile { get; private set; }

    /// <summary>Whether re-apply is offered.</summary>
    public bool CanReapply => LastProfile is not null;

    /// <summary>
    /// The re-apply button's text, with the profile name filled in.
    /// </summary>
    /// <remarks>
    /// Formatted here rather than bound straight to the resource: <c>Results_Reapply</c> is a
    /// template, and binding it directly renders "Re-apply {0}" with the braces showing.
    /// </remarks>
    public string ReapplyLabel => LastProfile is { } target
        ? _localizer.Format("Results_Reapply", target.ProfileId)
        : _localizer["Results_ReapplyNone"];

    /// <summary>
    /// Fills the screen from a finished run. Cleanup totals come from the tweak instances, which
    /// is why the apply step hands them back rather than rebuilding the profile.
    /// </summary>
    public void Load(ProfileApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _applyReport = result.Report;

        _freedBytes = 0;
        _lockedFiles = 0;

        foreach (var detail in result.Tweaks.OfType<CleanupTweak>().Select(tweak => tweak.LastApply))
        {
            if (detail is null)
            {
                continue;
            }

            _freedBytes += detail.BytesFreed;
            _lockedFiles += detail.FilesLocked;
        }

        RefreshReapply();

        Raise(nameof(AppliedCount));
        Raise(nameof(FailedCount));
        Raise(nameof(RequiresRestart));
        Raise(nameof(NothingApplied));
        Raise(nameof(FreedSpace));
        Raise(nameof(FreedBytes));
        Raise(nameof(LockedFiles));
    }

    /// <summary>Re-reads the log for a re-appliable profile. Also valid with no run loaded.</summary>
    public void RefreshReapply()
    {
        try
        {
            LastProfile = _reapply.FindLast();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            LastProfile = null;
        }

        Raise(nameof(LastProfile));
        Raise(nameof(CanReapply));
        Raise(nameof(ReapplyLabel));
    }

    /// <summary>
    /// Puts everything back. A partial failure is surfaced rather than swallowed: doc 5.3 says
    /// keep going and then show a clear list of what failed.
    /// </summary>
    public async Task UndoAllAsync()
    {
        IsUndoing = true;
        Error = null;
        UndoFailures.Clear();
        PermanentChanges.Clear();

        try
        {
            var report = await _undo.UndoAllAsync().ConfigureAwait(true);

            foreach (var failure in report.Failures)
            {
                UndoFailures.Add(new UndoFailureRow(failure.Record?.Target ?? failure.RecordId, failure.Reason));
            }

            foreach (var record in report.Permanent)
            {
                PermanentChanges.Add(record.Target);
            }

            UndoResult = report;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsUndoing = false;
            RefreshReapply();
        }
    }
}
