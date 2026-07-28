using System.Collections.ObjectModel;
using System.Globalization;
using SystevoTune.Engine.Cleanup;
using SystevoTune.Engine.Metrics;
using SystevoTune.Engine.Profiles;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.App.ViewModels;

/// <summary>One cleanup group on the scan screen.</summary>
public sealed class CleanupGroupRow(string name, string humanSize, int fileCount, long bytes)
{
    /// <summary>Group name in the current language.</summary>
    public string Name { get; } = name;

    /// <summary>Size found, already formatted.</summary>
    public string HumanSize { get; } = humanSize;

    /// <summary>Files found.</summary>
    public int FileCount { get; } = fileCount;

    /// <summary>Raw size, for totals and sorting.</summary>
    public long Bytes { get; } = bytes;
}

/// <summary>One tweak's current state on the scan screen.</summary>
public sealed class TweakStateRow(string name, TweakStatus status, string detail)
{
    /// <summary>Tweak name.</summary>
    public string Name { get; } = name;

    /// <summary>What the preview found.</summary>
    public TweakStatus Status { get; } = status;

    /// <summary>Old → new, or the reason it will not run.</summary>
    public string Detail { get; } = detail;

    /// <summary>Whether applying would change this. Drives the highlight.</summary>
    public bool WillChange => Status is TweakStatus.Ready;
}

/// <summary>
/// Screen 1. Everything in preview mode — this screen never changes anything.
/// </summary>
/// <remarks>
/// It runs the same <see cref="TweakRunner.PreviewAsync"/> the apply flow re-runs later, so what
/// the user reads here is produced by the code that will act on it, not a second description that
/// could drift.
/// </remarks>
public sealed class ScanViewModel : ObservableObject
{
    private readonly CleanupModule _cleanup;
    private readonly TweakRunner _runner;
    private readonly ProfileBuilder _builder;
    private readonly ProfileCatalog _profiles;
    private readonly MetricsCollector? _metrics;

    private bool _isBusy;
    private string? _error;
    private string _totalFreeable = "0 B";
    private long _totalFreeableBytes;
    private SystemSnapshot? _before;
    private bool _hasScanned;

    /// <param name="cleanup">Cleanup scanner.</param>
    /// <param name="runner">Preview runner.</param>
    /// <param name="builder">Turns a profile into tweaks.</param>
    /// <param name="profiles">Available presets.</param>
    /// <param name="metrics">Optional before-values. Null simply hides that panel.</param>
    public ScanViewModel(
        CleanupModule cleanup,
        TweakRunner runner,
        ProfileBuilder builder,
        ProfileCatalog profiles,
        MetricsCollector? metrics = null)
    {
        _cleanup = cleanup;
        _runner = runner;
        _builder = builder;
        _profiles = profiles;
        _metrics = metrics;

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
    }

    /// <summary>Cleanup groups and their sizes.</summary>
    public ObservableCollection<CleanupGroupRow> CleanupGroups { get; } = [];

    /// <summary>Each tweak's current state.</summary>
    public ObservableCollection<TweakStateRow> Tweaks { get; } = [];

    /// <summary>Runs the scan.</summary>
    public AsyncRelayCommand ScanCommand { get; }

    /// <summary>Whether a scan is in flight.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                ScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Why the scan could not finish, or <c>null</c>.</summary>
    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>Total freeable size, formatted.</summary>
    public string TotalFreeable
    {
        get => _totalFreeable;
        private set => Set(ref _totalFreeable, value);
    }

    /// <summary>Total freeable size in bytes.</summary>
    public long TotalFreeableBytes
    {
        get => _totalFreeableBytes;
        private set => Set(ref _totalFreeableBytes, value);
    }

    /// <summary>Before-values for the results comparison. <c>null</c> when metrics are unavailable.</summary>
    public SystemSnapshot? Before
    {
        get => _before;
        private set
        {
            if (Set(ref _before, value))
            {
                Raise(nameof(HasMetrics));
                Raise(nameof(MemoryUsedDisplay));
                Raise(nameof(StartupAppsDisplay));
            }
        }
    }

    /// <summary>Whether there are before-values worth showing.</summary>
    public bool HasMetrics => Before is not null;

    /// <summary>
    /// Memory in use, e.g. <c>7.4 GB / 15.9 GB (46%)</c>.
    /// </summary>
    /// <remarks>
    /// Formatted here rather than in XAML, and deliberately as numbers and units only — no words
    /// — so the string needs no translation and the view model stays testable without a localizer.
    /// <para>
    /// Empty when Windows would not answer. Doc 01 rules out invented numbers, and a memory
    /// reading is the easiest place in the app to accidentally show one.
    /// </para>
    /// </remarks>
    public string MemoryUsedDisplay => Before?.Memory is { } memory
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{CleanupScanReport.Humanise(memory.UsedBytes)} / {CleanupScanReport.Humanise(memory.TotalBytes)} ({memory.UsedPercent:0}%)")
        : string.Empty;

    /// <summary>Startup apps, e.g. <c>5 / 12</c> — enabled over total.</summary>
    public string StartupAppsDisplay => Before is { } snapshot
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{snapshot.EnabledStartupApps} / {snapshot.TotalStartupApps}")
        : string.Empty;

    /// <summary>Whether a scan has completed at least once. Distinguishes "empty" from "not run".</summary>
    public bool HasScanned
    {
        get => _hasScanned;
        private set => Set(ref _hasScanned, value);
    }

    /// <summary>There is genuinely nothing to clean.</summary>
    public bool NothingToClean => HasScanned && TotalFreeableBytes == 0;

    /// <summary>Every tweak is already where the profile wants it.</summary>
    public bool NothingToChange => HasScanned && Tweaks.Count > 0 && Tweaks.All(row => !row.WillChange);

    /// <summary>The whole scan came back empty.</summary>
    public bool IsEmpty => HasScanned && CleanupGroups.Count == 0 && Tweaks.Count == 0;

    /// <summary>
    /// Reads the machine. A failure anywhere becomes <see cref="Error"/> rather than an exception:
    /// a scan that cannot finish should say so, not take the window down.
    /// </summary>
    public async Task ScanAsync()
    {
        IsBusy = true;
        Error = null;

        try
        {
            CleanupGroups.Clear();
            Tweaks.Clear();

            var scan = _cleanup.Scan();
            foreach (var group in scan.Groups)
            {
                CleanupGroups.Add(new CleanupGroupRow(group.NameEn, group.HumanSize, group.FileCount, group.TotalBytes));
            }

            TotalFreeableBytes = scan.TotalBytes;
            TotalFreeable = scan.HumanTotal;

            // Preview against the first profile, purely to read current state. Nothing is applied.
            var profile = _profiles.Profiles.Count > 0 ? _profiles.Profiles[0] : null;
            if (profile is not null)
            {
                var preview = await _runner.PreviewAsync(_builder.Build(profile)).ConfigureAwait(true);

                foreach (var plan in preview.Plans)
                {
                    Tweaks.Add(new TweakStateRow(plan.TweakName, plan.Status, Describe(plan)));
                }
            }

            Before = _metrics?.Take();
            HasScanned = true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Raise(nameof(NothingToClean));
            Raise(nameof(NothingToChange));
            Raise(nameof(IsEmpty));
        }
    }

    private static string Describe(TweakPlan plan)
    {
        if (plan.Changes.Count > 0)
        {
            return string.Join("; ", plan.Changes.Select(change => change.Description));
        }

        return plan.Message ?? string.Empty;
    }
}
