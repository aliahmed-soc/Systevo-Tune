using SystevoTune.App.Localization;
using SystevoTune.Engine;
using SystevoTune.Engine.Safety;

namespace SystevoTune.App.ViewModels;

/// <summary>
/// B4. Language, where the logs live, and whether a restore point is made before applying.
/// </summary>
/// <remarks>
/// The restore point toggle is the only setting here that changes what the app does. It defaults
/// on, warns when switched off, and — the part that matters — the choice is written into the
/// change log for every run, so a log read months later says whether the safety net was there.
/// </remarks>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private bool _createRestorePoint = true;

    /// <param name="localizer">Language switch.</param>
    /// <param name="log">Read for the log folder path.</param>
    public SettingsViewModel(ILocalizer localizer, ChangeLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        _localizer = localizer;
        LogFolder = log.DirectoryPath;
    }

    /// <summary>Languages offered.</summary>
    public IReadOnlyList<Language> Languages => Language.All;

    /// <summary>Where the logs are written.</summary>
    public string LogFolder { get; }

    /// <summary>The engine version, for the about line.</summary>
    public string EngineVersion => EngineInfo.Version;

    /// <summary>
    /// Systevo's copyright line, in the current language.
    /// </summary>
    /// <remarks>
    /// Read through the localizer rather than hard-coded so it obeys the same rules as every
    /// other string — including the XAML scanner that fails the build on literals.
    /// </remarks>
    public string Copyright => _localizer["App_Copyright"];

    /// <summary>The language in use.</summary>
    public Language CurrentLanguage
    {
        get => _localizer.Current;
        set
        {
            if (value is not null && value.Code != _localizer.Current.Code)
            {
                _localizer.Use(value);
                Raise();
            }
        }
    }

    /// <summary>
    /// Whether to create a restore point before applying. Defaults on.
    /// </summary>
    public bool CreateRestorePoint
    {
        get => _createRestorePoint;
        set
        {
            if (Set(ref _createRestorePoint, value))
            {
                Raise(nameof(ShowRestorePointWarning));
            }
        }
    }

    /// <summary>Whether to show the warning that comes with switching restore points off.</summary>
    public bool ShowRestorePointWarning => !CreateRestorePoint;

    /// <summary>
    /// Writes the current setting into a run's log, so the record of what happened includes
    /// whether the safety net was switched on.
    /// </summary>
    /// <remarks>
    /// Recorded as metadata, not as a change: there is nothing to undo about a preference, and
    /// listing it among the undoable records would be nonsense.
    /// </remarks>
    public void RecordInto(ChangeLogRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        run.RecordChange(
            ChangeRecord.MetadataModule,
            "RestorePointSetting",
            "CreateRestorePointBeforeApply",
            null,
            CreateRestorePoint ? "on" : "off",
            undoable: false);
    }
}
