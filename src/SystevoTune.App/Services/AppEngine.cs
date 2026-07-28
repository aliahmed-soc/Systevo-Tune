using SystevoTune.App.Localization;
using SystevoTune.App.ViewModels;
using SystevoTune.Engine.Bloatware;
using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Metrics;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Platform.Windows;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Startup;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.App.Services;

/// <summary>
/// Wires the real Windows implementations to the engine, for the app.
/// </summary>
/// <remarks>
/// The one place in the app that constructs anything Windows-specific — the mirror of
/// <c>EngineHost</c> in the ConsoleRunner. Everything above this line takes interfaces or engine
/// types, which is what lets the view models be tested with Fakes and no UI thread.
/// </remarks>
public sealed class AppEngine
{
    private AppEngine(
        ChangeLog log,
        CleanupModule cleanup,
        TweakRunner runner,
        ProfileBuilder builder,
        ProfileApplier applier,
        ProfileCatalog profiles,
        ReapplyService reapply,
        UndoEngine undo,
        IRestorePointService restorePoints,
        MetricsCollector metrics)
    {
        Log = log;
        Cleanup = cleanup;
        Runner = runner;
        Builder = builder;
        Applier = applier;
        Profiles = profiles;
        Reapply = reapply;
        Undo = undo;
        RestorePoints = restorePoints;
        Metrics = metrics;
    }

    /// <summary>The change log.</summary>
    public ChangeLog Log { get; }

    /// <summary>Cleanup scanning and tweaks.</summary>
    public CleanupModule Cleanup { get; }

    /// <summary>Preview and apply.</summary>
    public TweakRunner Runner { get; }

    /// <summary>Turns a profile into tweaks.</summary>
    public ProfileBuilder Builder { get; }

    /// <summary>Applies a profile and records which one it was.</summary>
    public ProfileApplier Applier { get; }

    /// <summary>The presets.</summary>
    public ProfileCatalog Profiles { get; }

    /// <summary>Finds the last applied profile.</summary>
    public ReapplyService Reapply { get; }

    /// <summary>Undo All and per-item undo.</summary>
    public UndoEngine Undo { get; }

    /// <summary>Restore points.</summary>
    public IRestorePointService RestorePoints { get; }

    /// <summary>Before/after numbers.</summary>
    public MetricsCollector Metrics { get; }

    /// <summary>Builds everything against the real machine.</summary>
    public static AppEngine Create()
    {
        var registry = new WindowsRegistryService();
        var files = new WindowsFileSystemService();
        var environment = new WindowsEnvironmentPaths();
        var processes = new ProcessRunner();
        var powerPlans = new PowerCfgPowerPlanService(processes);
        var appPackages = new PowerShellAppPackageService(processes);
        var services = new ScServiceController(processes);

        var cleanup = new CleanupModule(CleanupWhitelist.Load(), files, environment, services);
        var registryTweaks = RegistryTweakCatalog.Load();
        var builder = new ProfileBuilder(
            cleanup, registryTweaks, registry, powerPlans, PowerPlanCatalog.Load(), new SystemBatteryStatus());

        var log = ChangeLog.Default();
        var runner = new TweakRunner();
        var profiles = ProfileCatalog.Load();
        var startup = new StartupManager(StartupLocationCatalog.Load(), registry, files, environment);

        IUndoHandler[] undoHandlers =
        [
            new RegistryUndoHandler(registry),
            new PowerPlanUndoHandler(powerPlans),
            new BloatwareUndoHandler(appPackages),
        ];

        return new AppEngine(
            log,
            cleanup,
            runner,
            builder,
            new ProfileApplier(builder, runner),
            profiles,
            new ReapplyService(log, profiles),
            new UndoEngine(log, undoHandlers),
            new RestorePointService(registry, processes),
            new MetricsCollector(new WindowsSystemMetrics(), startup, cleanup));
    }

    /// <summary>Builds the shell view model over this engine.</summary>
    public ShellViewModel CreateShell(ILocalizer localizer)
        => new(
            localizer,
            new ScanViewModel(Cleanup, Runner, Builder, Profiles, Metrics),
            new ReviewViewModel(Runner, Builder, Profiles),
            new ResultsViewModel(Undo, Reapply, localizer),
            new LogViewerViewModel(Log, localizer),
            new SettingsViewModel(localizer, Log));
}
