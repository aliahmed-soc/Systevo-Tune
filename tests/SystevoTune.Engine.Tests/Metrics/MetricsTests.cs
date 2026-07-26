using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Metrics;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Startup;
using SystevoTune.Engine.Tests.Fakes;

namespace SystevoTune.Engine.Tests.Metrics;

/// <summary>A memory reading a test can set.</summary>
internal sealed class FakeSystemMetrics(MemoryReading? reading = null) : ISystemMetrics
{
    public MemoryReading? Reading { get; set; } = reading;

    public MemoryReading? ReadMemory() => Reading;
}

public class MetricsTests
{
    private const string UserTemp = @"C:\FakeUsers\tester\AppData\Local\Temp";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly FakeRegistryService _registry = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeEnvironmentPaths _environment = new();
    private readonly FakeSystemMetrics _metrics = new(new MemoryReading(16_000_000_000, 8_000_000_000));

    private MetricsCollector Collector() => new(
        _metrics,
        new StartupManager(StartupLocationCatalog.Load(), _registry, _files, _environment),
        new CleanupModule(CleanupWhitelist.Load(), _files, _environment));

    private static RegistryValueRef Run(string name) => new(RegistryRoot.CurrentUser, RunKey, name);

    private static RegistryValueRef Approved(string name) => new(RegistryRoot.CurrentUser, ApprovedKey, name);

    // ---- memory ----

    [Fact]
    public void Memory_in_use_is_total_minus_available()
    {
        var reading = new MemoryReading(16_000_000_000, 6_000_000_000);

        Assert.Equal(10_000_000_000, reading.UsedBytes);
        Assert.Equal(62.5, reading.UsedPercent);
    }

    [Fact]
    public void A_machine_reporting_no_memory_does_not_divide_by_zero()
        => Assert.Equal(0, new MemoryReading(0, 0).UsedPercent);

    [Fact]
    public void A_memory_reading_that_fails_leaves_the_rest_of_the_snapshot_intact()
    {
        _metrics.Reading = null;
        _files.WithFile($@"{UserTemp}\a.tmp", 4096);

        var snapshot = Collector().Take();

        Assert.Null(snapshot.Memory);
        Assert.Equal(4096, snapshot.CleanableBytes);
    }

    // ---- startup counts ----

    [Fact]
    public void The_snapshot_counts_enabled_and_total_startup_apps()
    {
        _registry.With(Run("OneDrive"), RegistryValue.Text("a.exe"));
        _registry.With(Run("Spotify"), RegistryValue.Text("b.exe"));
        _registry.With(Approved("Spotify"), RegistryValue.Binary([0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

        var snapshot = Collector().Take();

        Assert.Equal(2, snapshot.TotalStartupApps);
        Assert.Equal(1, snapshot.EnabledStartupApps);
    }

    // ---- cleanable size ----

    [Fact]
    public void The_snapshot_reports_what_cleanup_could_free()
    {
        _files.WithFile($@"{UserTemp}\a.tmp", 1536);

        Assert.Equal("1.5 KB", Collector().Take().HumanCleanable);
    }

    // ---- comparison ----

    [Fact]
    public void Freed_space_is_the_drop_in_what_cleanup_can_see()
    {
        var before = new SystemSnapshot(null, 5, 8, 3_000_000);
        var after = new SystemSnapshot(null, 5, 8, 1_000_000);

        Assert.Equal(2_000_000, new SnapshotComparison(before, after).BytesFreed);
    }

    [Fact]
    public void Junk_reappearing_between_runs_never_reports_negative_freed_space()
    {
        var before = new SystemSnapshot(null, 5, 8, 1_000_000);
        var after = new SystemSnapshot(null, 5, 8, 3_000_000);

        Assert.Equal(0, new SnapshotComparison(before, after).BytesFreed);
    }

    [Fact]
    public void Startup_apps_disabled_is_the_drop_in_enabled_apps()
    {
        var before = new SystemSnapshot(null, 8, 10, 0);
        var after = new SystemSnapshot(null, 3, 10, 0);

        Assert.Equal(5, new SnapshotComparison(before, after).StartupAppsDisabled);
    }

    [Fact]
    public void Memory_freed_is_null_when_either_reading_is_missing()
    {
        var before = new SystemSnapshot(new MemoryReading(16, 8), 0, 0, 0);
        var after = new SystemSnapshot(null, 0, 0, 0);

        Assert.Null(new SnapshotComparison(before, after).MemoryFreed);
    }

    [Fact]
    public void Memory_freed_is_the_drop_in_ram_in_use()
    {
        var before = new SystemSnapshot(new MemoryReading(16_000, 6_000), 0, 0, 0);
        var after = new SystemSnapshot(new MemoryReading(16_000, 9_000), 0, 0, 0);

        Assert.Equal(3_000, new SnapshotComparison(before, after).MemoryFreed);
    }
}
