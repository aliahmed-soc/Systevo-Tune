using System.Collections.ObjectModel;
using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.App.ViewModels;

/// <summary>How one tweak ended, for the live list.</summary>
public enum ApplyOutcomeKind
{
    /// <summary>Everything it planned was applied.</summary>
    Applied,

    /// <summary>Nothing to do — already set, or not available here.</summary>
    NothingToDo,

    /// <summary>Something was skipped on purpose, with a reason.</summary>
    Warning,

    /// <summary>At least one change failed.</summary>
    Failed,
}

/// <summary>One line in the live apply list.</summary>
public sealed class ApplyOutcomeRow(string tweakName, ApplyOutcomeKind kind, int appliedCount, string? detail)
{
    /// <summary>Tweak name.</summary>
    public string TweakName { get; } = tweakName;

    /// <summary>How it ended.</summary>
    public ApplyOutcomeKind Kind { get; } = kind;

    /// <summary>How many changes went through.</summary>
    public int AppliedCount { get; } = appliedCount;

    /// <summary>Reason or extra information. May be <c>null</c>.</summary>
    public string? Detail { get; } = detail;
}

/// <summary>
/// Screen 3. Streams the engine's results as they happen.
/// </summary>
/// <remarks>
/// The rows arrive through <see cref="IProgress{T}"/> from the runner, so a long apply shows
/// progress instead of freezing on a spinner. Needs-restart flags are gathered as they come and
/// reported once at the end rather than nagging per tweak.
/// </remarks>
public sealed class ApplyViewModel : ObservableObject, IDisposable
{
    private readonly ProfileApplier _applier;
    private readonly ChangeLog _log;
    private readonly CancellationTokenSource _cancellation = new();

    private bool _isRunning;
    private bool _isFinished;
    private string? _currentTweak;
    private string? _error;
    private ProfileApplyResult? _result;

    private readonly SynchronizationContext? _uiContext;

    /// <param name="applier">Applies a profile and records which one it was.</param>
    /// <param name="log">The change log to open a run in.</param>
    public ApplyViewModel(ProfileApplier applier, ChangeLog log)
        : this(applier, log, SynchronizationContext.Current)
    {
    }

    /// <param name="applier">Applies a profile and records which one it was.</param>
    /// <param name="log">The change log to open a run in.</param>
    /// <param name="uiContext">
    /// Where progress callbacks run. <c>null</c> runs them inline. Tests pass <c>null</c>
    /// deliberately: xUnit installs its own <see cref="SynchronizationContext"/>, so capturing
    /// whatever happens to be current would defer the callbacks and race with the assertions.
    /// </param>
    internal ApplyViewModel(ProfileApplier applier, ChangeLog log, SynchronizationContext? uiContext)
    {
        _applier = applier;
        _log = log;
        _uiContext = uiContext;

        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
    }

    /// <summary>Results as they arrive.</summary>
    public ObservableCollection<ApplyOutcomeRow> Outcomes { get; } = [];

    /// <summary>Stops the run between tweaks.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Whether the run is in flight.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Whether the run has finished, however it ended.</summary>
    public bool IsFinished
    {
        get => _isFinished;
        private set => Set(ref _isFinished, value);
    }

    /// <summary>The tweak being worked on, for the status line.</summary>
    public string? CurrentTweak
    {
        get => _currentTweak;
        private set => Set(ref _currentTweak, value);
    }

    /// <summary>Why the run could not finish at all, or <c>null</c>.</summary>
    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>The finished run, for the results screen. <c>null</c> until it completes.</summary>
    public ProfileApplyResult? Result
    {
        get => _result;
        private set => Set(ref _result, value);
    }

    /// <summary>The run id, so the results screen can name it.</summary>
    public string? RunId { get; private set; }

    /// <summary>Tweaks that need a restart, gathered as the run goes.</summary>
    public ObservableCollection<string> NeedsRestart { get; } = [];

    /// <summary>Whether anything in this run needs a restart.</summary>
    public bool RequiresRestart => NeedsRestart.Count > 0;

    /// <summary>Whether the user stopped it.</summary>
    public bool WasCancelled => Result?.Report.Cancelled ?? false;

    /// <summary>
    /// Applies the profile, streaming each tweak's outcome as it lands.
    /// </summary>
    /// <param name="profile">The preset to apply.</param>
    /// <param name="onRunStarted">
    /// Runs once the log run is open and before any tweak does. Settings uses it to record
    /// whether a restore point was wanted, so the log for this run says whether the safety net
    /// was there — which only means anything if it is written before the changes are.
    /// </param>
    public async Task RunAsync(Profile profile, Action<ChangeLogRun>? onRunStarted = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        IsRunning = true;
        IsFinished = false;
        Error = null;
        Outcomes.Clear();
        NeedsRestart.Clear();

        try
        {
            var run = _log.StartRun();
            RunId = run.RunId;
            Raise(nameof(RunId));

            onRunStarted?.Invoke(run);

            var progress = new MarshalledProgress<TweakOutcome>(Record, _uiContext);

            Result = await _applier
                .ApplyAsync(profile, run, progress, _cancellation.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancelling is a choice, not a fault. Whatever was applied is in the log.
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsRunning = false;
            IsFinished = true;
            CurrentTweak = null;
            Raise(nameof(RequiresRestart));
            Raise(nameof(WasCancelled));
        }
    }

    /// <summary>Turns an engine outcome into a display row. Internal so tests can drive it directly.</summary>
    internal void Record(TweakOutcome outcome)
    {
        CurrentTweak = outcome.TweakName;

        if (outcome.RequiresRestart && outcome.Applied.Count > 0)
        {
            NeedsRestart.Add(outcome.TweakName);
            Raise(nameof(RequiresRestart));
        }

        Outcomes.Add(new ApplyOutcomeRow(
            outcome.TweakName,
            Classify(outcome),
            outcome.Applied.Count,
            DetailFor(outcome)));
    }

    /// <summary>
    /// A cleanup group that skipped itself is a warning, not a failure — nothing went wrong, the
    /// safe thing happened. Decision H1 depends on that distinction reaching the user intact.
    /// </summary>
    private static ApplyOutcomeKind Classify(TweakOutcome outcome)
    {
        if (outcome.Failures.Count > 0)
        {
            return ApplyOutcomeKind.Failed;
        }

        if (outcome.Applied.Count == 0)
        {
            return ApplyOutcomeKind.NothingToDo;
        }

        return ApplyOutcomeKind.Applied;
    }

    private static string? DetailFor(TweakOutcome outcome)
    {
        if (outcome.Failures.Count > 0)
        {
            return string.Join("; ", outcome.Failures.Select(failure => failure.Reason));
        }

        return outcome.Message;
    }

    /// <summary>Marks a cleanup skip on an already-recorded row, once the tweak instance is known.</summary>
    internal void MarkCleanupSkips(IReadOnlyList<ITweak> tweaks)
    {
        foreach (var cleanup in tweaks.OfType<CleanupTweak>())
        {
            if (cleanup.LastApply is not { WasSkipped: true } detail)
            {
                continue;
            }

            var index = Outcomes.ToList().FindIndex(row => row.TweakName == cleanup.Name);
            if (index >= 0)
            {
                Outcomes[index] = new ApplyOutcomeRow(cleanup.Name, ApplyOutcomeKind.Warning, 0, detail.SkippedReason);
            }
        }
    }

    /// <summary>
    /// Releases the cancellation source.
    /// </summary>
    /// <remarks>
    /// A fresh view model is created per apply run (the shell hands one to
    /// <see cref="ShellViewModel.BeginApply"/> each time), so without this every run would leak a
    /// <see cref="CancellationTokenSource"/> and the timer handle inside it.
    /// </remarks>
    public void Dispose() => _cancellation.Dispose();

    private void Cancel() => _cancellation.Cancel();
}
