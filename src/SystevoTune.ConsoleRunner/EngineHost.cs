using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Platform.Windows;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Startup;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Power;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.ConsoleRunner;

/// <summary>
/// Wires the real Windows implementations to the engine. The only place in the repo that does,
/// which is why the engine itself stays testable.
/// </summary>
internal sealed class EngineHost
{
    private EngineHost(
        ChangeLog log,
        CleanupModule cleanup,
        StartupManager startup,
        ProfileCatalog profiles,
        ProfileBuilder profileBuilder,
        IRestorePointService restorePoints,
        IReadOnlyList<IUndoHandler> undoHandlers,
        IElevation elevation)
    {
        Log = log;
        Cleanup = cleanup;
        Startup = startup;
        Profiles = profiles;
        ProfileBuilder = profileBuilder;
        RestorePoints = restorePoints;
        UndoHandlers = undoHandlers;
        Elevation = elevation;
    }

    public ChangeLog Log { get; }

    public CleanupModule Cleanup { get; }

    public StartupManager Startup { get; }

    public ProfileCatalog Profiles { get; }

    public ProfileBuilder ProfileBuilder { get; }

    public IRestorePointService RestorePoints { get; }

    public IReadOnlyList<IUndoHandler> UndoHandlers { get; }

    public IElevation Elevation { get; }

    public TweakRunner Runner { get; } = new();

    /// <summary>An undo engine over the shipped handlers.</summary>
    public UndoEngine NewUndoEngine() => new(Log, UndoHandlers);

    /// <summary>Builds a host talking to the real machine.</summary>
    public static EngineHost Create()
    {
        var registry = new WindowsRegistryService();
        var files = new WindowsFileSystemService();
        var environment = new WindowsEnvironmentPaths();
        var processes = new ProcessRunner();
        var powerPlans = new PowerCfgPowerPlanService(processes);
        var powerPlanCatalog = PowerPlanCatalog.Load();
        var registryTweaks = RegistryTweakCatalog.Load();
        var cleanup = new CleanupModule(
            CleanupWhitelist.Load(), files, environment, new ScServiceController(processes));

        return new EngineHost(
            ChangeLog.Default(),
            cleanup,
            new StartupManager(StartupLocationCatalog.Load(), registry, files, environment),
            ProfileCatalog.Load(),
            new ProfileBuilder(cleanup, registryTweaks, registry, powerPlans, powerPlanCatalog, new SystemBatteryStatus()),
            new RestorePointService(registry, processes),
            [new RegistryUndoHandler(registry), new PowerPlanUndoHandler(powerPlans)],
            new WindowsElevation());
    }
}
