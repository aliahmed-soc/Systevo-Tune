using System.Globalization;

namespace SystevoTune.Engine.Cleanup;

/// <summary>What one group holds right now.</summary>
/// <param name="GroupId">Whitelist group id.</param>
/// <param name="NameEn">English name.</param>
/// <param name="NameAr">Arabic name.</param>
/// <param name="FileCount">Files found.</param>
/// <param name="TotalBytes">Total size found.</param>
/// <param name="MissingPaths">Whitelist paths that do not exist on this PC. Not an error.</param>
/// <param name="RejectedPaths">
/// Whitelist paths refused by the safety guard, with the reason. Should always be empty; anything
/// here means the whitelist file needs fixing.
/// </param>
public sealed record CleanupGroupScan(
    string GroupId,
    string NameEn,
    string NameAr,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<string> MissingPaths,
    IReadOnlyList<string> RejectedPaths)
{
    /// <summary>Size for display, e.g. <c>1.4 GB</c>.</summary>
    public string HumanSize => CleanupScanReport.Humanise(TotalBytes);
}

/// <summary>
/// The scan doc 3.1 requires: size per group, shown before anything is deleted.
/// </summary>
public sealed record CleanupScanReport(IReadOnlyList<CleanupGroupScan> Groups)
{
    /// <summary>Total across every scanned group.</summary>
    public long TotalBytes { get; } = Groups.Sum(group => group.TotalBytes);

    /// <summary>Files across every scanned group.</summary>
    public int TotalFiles { get; } = Groups.Sum(group => group.FileCount);

    /// <summary>Total size for display.</summary>
    public string HumanTotal => Humanise(TotalBytes);

    /// <summary>Bytes as a short human string. Rounded down, so the number never oversells.</summary>
    public static string Humanise(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var rounded = Math.Floor(size * 10) / 10;
        return unit == 0
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : rounded.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}

/// <summary>What an apply actually managed to remove.</summary>
/// <param name="GroupId">Whitelist group id.</param>
/// <param name="FilesDeleted">Files removed.</param>
/// <param name="BytesFreed">Size removed.</param>
/// <param name="FilesLocked">Files that were in use and left alone.</param>
/// <param name="SkippedReason">
/// Why nothing was deleted, or <c>null</c> if the group ran. Set when a service the group needs
/// stopped would not stop — decision H1 says skip with a warning rather than delete anyway.
/// </param>
public sealed record CleanupApplyDetail(
    string GroupId,
    int FilesDeleted,
    long BytesFreed,
    int FilesLocked,
    string? SkippedReason = null)
{
    /// <summary>Size freed, for display.</summary>
    public string HumanFreed => CleanupScanReport.Humanise(BytesFreed);

    /// <summary>Nothing was deleted, and <see cref="SkippedReason"/> says why.</summary>
    public bool WasSkipped => SkippedReason is not null;

    /// <summary>A group that was left alone.</summary>
    public static CleanupApplyDetail Skipped(string groupId, string reason)
        => new(groupId, 0, 0, 0, reason);
}
