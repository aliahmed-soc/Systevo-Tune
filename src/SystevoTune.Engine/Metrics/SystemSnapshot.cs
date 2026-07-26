using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Startup;

namespace SystevoTune.Engine.Metrics;

/// <summary>How much memory the PC has and how much is free.</summary>
/// <param name="TotalBytes">Physical RAM installed.</param>
/// <param name="AvailableBytes">Physical RAM not in use.</param>
public sealed record MemoryReading(long TotalBytes, long AvailableBytes)
{
    /// <summary>RAM in use.</summary>
    public long UsedBytes => TotalBytes - AvailableBytes;

    /// <summary>Percentage of RAM in use, rounded to one place.</summary>
    public double UsedPercent => TotalBytes <= 0 ? 0 : Math.Round(UsedBytes * 100.0 / TotalBytes, 1);
}

/// <summary>Reads live system measurements. Behind an interface so tests do not query the machine.</summary>
public interface ISystemMetrics
{
    /// <summary>Current memory. <c>null</c> when Windows would not say.</summary>
    MemoryReading? ReadMemory();
}

/// <summary>
/// The numbers doc 4 wants for a before/after comparison. Only measurements the engine can take
/// honestly — no invented "PC health" score (doc 01: no fake scare screens).
/// </summary>
/// <param name="Memory">Memory in use. <c>null</c> when it could not be read.</param>
/// <param name="EnabledStartupApps">Apps set to run at sign-in.</param>
/// <param name="TotalStartupApps">Apps present at all, enabled or not.</param>
/// <param name="CleanableBytes">What a cleanup scan says it could free right now.</param>
public sealed record SystemSnapshot(
    MemoryReading? Memory,
    int EnabledStartupApps,
    int TotalStartupApps,
    long CleanableBytes)
{
    /// <summary>Cleanable size for display.</summary>
    public string HumanCleanable => CleanupScanReport.Humanise(CleanableBytes);
}

/// <summary>The difference between two snapshots, for the results screen.</summary>
/// <param name="Before">Taken before the apply run.</param>
/// <param name="After">Taken after it.</param>
public sealed record SnapshotComparison(SystemSnapshot Before, SystemSnapshot After)
{
    /// <summary>Startup apps switched off. Negative if more were switched on.</summary>
    public int StartupAppsDisabled => Before.EnabledStartupApps - After.EnabledStartupApps;

    /// <summary>Disk space freed, from what cleanup can no longer see.</summary>
    public long BytesFreed => Math.Max(0, Before.CleanableBytes - After.CleanableBytes);

    /// <summary>Freed space for display.</summary>
    public string HumanFreed => CleanupScanReport.Humanise(BytesFreed);

    /// <summary>
    /// Change in RAM in use, in bytes. Positive means less RAM in use afterwards.
    /// <c>null</c> when either reading failed.
    /// </summary>
    /// <remarks>
    /// Worth showing with care: RAM in use moves on its own all the time, so a single pair of
    /// readings is weak evidence. Doc 01 rules out overselling, so the UI should present this as
    /// an observation, not a claimed win.
    /// </remarks>
    public long? MemoryFreed => Before.Memory is null || After.Memory is null
        ? null
        : Before.Memory.UsedBytes - After.Memory.UsedBytes;
}

/// <summary>Takes a snapshot of the measurements the engine can read.</summary>
public sealed class MetricsCollector(ISystemMetrics metrics, StartupManager startup, CleanupModule cleanup)
{
    /// <summary>Reads everything. Never throws for a measurement it cannot take.</summary>
    public SystemSnapshot Take(CancellationToken cancellationToken = default)
    {
        var startupItems = startup.List(cancellationToken);

        return new SystemSnapshot(
            metrics.ReadMemory(),
            startupItems.Count(item => item.State is StartupState.Enabled),
            startupItems.Count,
            cleanup.Scan(cancellationToken: cancellationToken).TotalBytes);
    }
}
