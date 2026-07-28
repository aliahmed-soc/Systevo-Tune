using System.Windows;
using SystevoTune.App.Localization;
using SystevoTune.App.Services;
using SystevoTune.App.ViewModels;
using SystevoTune.Engine.Profiles;

namespace SystevoTune.App;

/// <summary>The shell window. Holds the four screens from doc 06 Phase 3.</summary>
public partial class MainWindow : Window
{
    private readonly AppEngine _engine;
    private readonly ILocalizer _localizer;

    /// <summary>Builds the window over an engine.</summary>
    public MainWindow(AppEngine engine, ILocalizer localizer)
    {
        _engine = engine;
        _localizer = localizer;

        // Registered app-wide so views can bind text without threading the localizer through
        // every DataContext.
        Application.Current.Resources["Loc"] = localizer;

        Shell = engine.CreateShell(localizer);
        DataContext = Shell;

        InitializeComponent();

        Loaded += async (_, _) => await Shell.Scan.ScanAsync().ConfigureAwait(true);
    }

    /// <summary>The shell view model.</summary>
    public ShellViewModel Shell { get; }

    /// <summary>Applies what the user ticked on the Review screen.</summary>
    public Task ApplySelectedAsync()
        => Shell.Review.SelectedProfile is { } profile && Shell.Review.CanApply
            ? RunProfileAsync(profile, Shell.Review.SelectedCount)
            : Task.CompletedTask;

    /// <summary>
    /// Runs the last applied profile again (doc 5.6 — Windows updates reset tweaks).
    /// </summary>
    /// <remarks>
    /// Goes through exactly the same confirm dialog and restore point as a first apply. It is
    /// still a change to the machine, and "you already agreed to this once" is not consent for
    /// doing it again a month later.
    /// </remarks>
    public async Task ReapplyLastAsync()
    {
        if (Shell.Results.LastProfile is not { } target
            || _engine.Profiles.Find(target.ProfileId) is not { } profile)
        {
            return;
        }

        // Re-planned against the live system, so the count is what would actually change now —
        // not what changed the first time. Usually far smaller.
        var preview = await _engine.Runner
            .PreviewAsync(_engine.Builder.Build(profile))
            .ConfigureAwait(true);

        if (preview.AllChanges.Count == 0)
        {
            // Nothing was reset, so there is nothing to re-apply. Opening a confirm dialog for
            // zero changes would be asking permission to do nothing.
            MessageBox.Show(
                this,
                _localizer["Review_NothingToDo"],
                _localizer["App_Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        await RunProfileAsync(profile, preview.AllChanges.Count).ConfigureAwait(true);
    }

    /// <summary>
    /// The two-step apply: confirm dialog first, then the apply screen, then results.
    /// </summary>
    /// <remarks>
    /// A6. The dialog attempts the restore point while it is open, so the user decides with the
    /// answer in front of them. Cancelling here changes nothing at all.
    /// <para>
    /// One path for both Apply and Re-apply on purpose — two copies would be two places for the
    /// restore point step to go missing.
    /// </para>
    /// </remarks>
    private async Task RunProfileAsync(Profile profile, int changeCount)
    {
        var confirm = new ConfirmApplyViewModel(
            _engine.RestorePoints,
            changeCount,
            $"Systevo Tune: before {profile.NameEn}",
            Shell.Settings.CreateRestorePoint);

        var dialog = new Views.ConfirmApplyDialog(confirm, _localizer) { Owner = this };

        if (dialog.ShowDialog() != true || !confirm.Confirmed)
        {
            return;
        }

        var apply = Shell.BeginApply(new ApplyViewModel(_engine.Applier, _engine.Log));

        // The restore point choice is written into the run before any change is, so a log read
        // later says whether the safety net was switched on for it.
        await apply.RunAsync(profile, Shell.Settings.RecordInto).ConfigureAwait(true);

        if (apply.Result is { } result)
        {
            apply.MarkCleanupSkips(result.Tweaks);
        }

        Shell.ShowResults();
    }
}
