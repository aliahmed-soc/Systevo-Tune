using SystevoTune.App.Localization;

namespace SystevoTune.App.ViewModels;

/// <summary>Which screen the shell is showing.</summary>
public enum AppScreen
{
    /// <summary>Screen 1 — read-only look at the PC.</summary>
    Scan,

    /// <summary>Screen 2 — tick what to change.</summary>
    Review,

    /// <summary>Screen 3 — live apply.</summary>
    Apply,

    /// <summary>Screen 4 — summary and Undo All.</summary>
    Results,

    /// <summary>Past runs, read from the log files.</summary>
    Logs,

    /// <summary>Language, log folder, restore point toggle.</summary>
    Settings,
}

/// <summary>
/// The window. Owns which screen is showing and the language.
/// </summary>
/// <remarks>
/// The four screens follow doc 06 Phase 3 in order: scan, review, apply, results. Moving forward
/// from Review goes through the confirm dialog first (A6), which is why the shell exposes
/// <see cref="BeginApply"/> rather than letting Review jump straight to Apply.
/// </remarks>
public sealed class ShellViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private AppScreen _screen = AppScreen.Scan;

    /// <param name="localizer">Text lookup, also the language switch.</param>
    /// <param name="scan">Screen 1.</param>
    /// <param name="review">Screen 2.</param>
    /// <param name="results">Screen 4.</param>
    public ShellViewModel(
        ILocalizer localizer,
        ScanViewModel scan,
        ReviewViewModel review,
        ResultsViewModel results,
        LogViewerViewModel logs,
        SettingsViewModel settings)
    {
        _localizer = localizer;
        Scan = scan;
        Review = review;
        Results = results;
        Logs = logs;
        Settings = settings;

        GoToScanCommand = new RelayCommand(() => Screen = AppScreen.Scan);
        GoToReviewCommand = new RelayCommand(() => Screen = AppScreen.Review);
        GoToResultsCommand = new RelayCommand(() => Screen = AppScreen.Results);
        GoToLogsCommand = new RelayCommand(async () =>
        {
            Screen = AppScreen.Logs;
            await Logs.RefreshAsync().ConfigureAwait(true);
        });
        GoToSettingsCommand = new RelayCommand(() => Screen = AppScreen.Settings);
        ToggleLanguageCommand = new RelayCommand(() => CurrentLanguage = OtherLanguage);
    }

    /// <summary>Text lookup, bound throughout the XAML.</summary>
    public ILocalizer Localizer => _localizer;

    /// <summary>Screen 1.</summary>
    public ScanViewModel Scan { get; }

    /// <summary>Screen 2.</summary>
    public ReviewViewModel Review { get; }

    /// <summary>Screen 3. Created fresh for each run, so a second apply starts clean.</summary>
    public ApplyViewModel? Apply { get; private set; }

    /// <summary>Screen 4.</summary>
    public ResultsViewModel Results { get; }

    /// <summary>Past runs, read from the log files.</summary>
    public LogViewerViewModel Logs { get; }

    /// <summary>Language, log folder, restore point toggle.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Languages offered in the switch.</summary>
    public IReadOnlyList<Language> Languages => Language.All;

    /// <summary>Navigation.</summary>
    public RelayCommand GoToScanCommand { get; }

    /// <summary>Navigation.</summary>
    public RelayCommand GoToReviewCommand { get; }

    /// <summary>Navigation.</summary>
    public RelayCommand GoToResultsCommand { get; }

    /// <summary>Navigation.</summary>
    public RelayCommand GoToLogsCommand { get; }

    /// <summary>Navigation.</summary>
    public RelayCommand GoToSettingsCommand { get; }

    /// <summary>
    /// Switches to the other language. There are two, so this is a toggle rather than a list.
    /// </summary>
    /// <remarks>
    /// It was a <c>ComboBox</c> until the first VM run, where it rendered as an empty box: WPF's
    /// default combo template keeps its own light chrome regardless of the Background we set, and
    /// the theme's implicit TextBlock style painted the selected item near-white on it. A button
    /// uses the style already proven readable on every other control in the window.
    /// </remarks>
    public RelayCommand ToggleLanguageCommand { get; }

    /// <summary>
    /// The language the toggle would move to — the button is labelled with where it goes, not
    /// where it is, so it reads as an action.
    /// </summary>
    public Language OtherLanguage =>
        Language.All.FirstOrDefault(language => language.Code != _localizer.Current.Code)
            ?? _localizer.Current;

    /// <summary>The screen on show.</summary>
    public AppScreen Screen
    {
        get => _screen;
        set
        {
            if (Set(ref _screen, value))
            {
                Raise(nameof(IsScan));
                Raise(nameof(IsReview));
                Raise(nameof(IsApply));
                Raise(nameof(IsResults));
                Raise(nameof(IsLogs));
                Raise(nameof(IsSettings));
            }
        }
    }

    /// <summary>Screen 1 is showing.</summary>
    public bool IsScan => Screen is AppScreen.Scan;

    /// <summary>Screen 2 is showing.</summary>
    public bool IsReview => Screen is AppScreen.Review;

    /// <summary>Screen 3 is showing.</summary>
    public bool IsApply => Screen is AppScreen.Apply;

    /// <summary>Screen 4 is showing.</summary>
    public bool IsResults => Screen is AppScreen.Results;

    /// <summary>The log viewer is showing.</summary>
    public bool IsLogs => Screen is AppScreen.Logs;

    /// <summary>Settings is showing.</summary>
    public bool IsSettings => Screen is AppScreen.Settings;

    /// <summary>The language in use, for the switcher's selection.</summary>
    public Language CurrentLanguage
    {
        get => _localizer.Current;
        set
        {
            if (value is not null && value.Code != _localizer.Current.Code)
            {
                _localizer.Use(value);
                Raise();
                Raise(nameof(OtherLanguage));
            }
        }
    }

    /// <summary>
    /// Hands screen 3 a fresh view model and shows it. Called only after the confirm dialog has
    /// been accepted, never straight from Review.
    /// </summary>
    public ApplyViewModel BeginApply(ApplyViewModel apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        // Each run gets a fresh view model, so the one it replaces has to let go of its
        // cancellation source.
        Apply?.Dispose();
        Apply = apply;
        Raise(nameof(Apply));
        Screen = AppScreen.Apply;
        return apply;
    }

    /// <summary>Moves to the results screen with a finished run loaded.</summary>
    public void ShowResults()
    {
        if (Apply?.Result is { } result)
        {
            Results.Load(result);
        }
        else
        {
            Results.RefreshReapply();
        }

        Screen = AppScreen.Results;
    }
}
