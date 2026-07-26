using System.Globalization;
using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.Engine.Cleanup;

/// <summary>
/// Deleting junk from the whitelisted paths. Scan first, then preview, then apply — doc 3.1.
/// </summary>
/// <remarks>
/// Cleanup is the one module whose changes cannot be undone: a deleted temp file is gone. Its
/// records are written with <c>undoable: false</c>, so Undo lists them as permanent instead of
/// pretending to restore them. The restore point from doc 5.1 is the real safety net here.
/// </remarks>
public sealed class CleanupModule(
    CleanupWhitelist whitelist,
    IFileSystemService files,
    IEnvironmentPaths environment)
{
    /// <summary>Module name on every cleanup change record.</summary>
    public const string ModuleName = "Cleanup";

    /// <summary>Prefix for cleanup tweak ids, e.g. <c>cleanup.temp-files</c>.</summary>
    public const string TweakIdPrefix = "cleanup.";

    /// <summary>
    /// Measures every group without deleting anything. This is what the user sees first.
    /// </summary>
    public CleanupScanReport Scan(IEnumerable<string>? groupIds = null, CancellationToken cancellationToken = default)
    {
        var wanted = groupIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scans = new List<CleanupGroupScan>();

        foreach (var group in whitelist.Groups)
        {
            if (wanted is not null && !wanted.Contains(group.Id))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            scans.Add(ScanGroup(group, cancellationToken));
        }

        return new CleanupScanReport(scans);
    }

    /// <summary>One tweak per whitelist group, so the user can tick them individually.</summary>
    public IReadOnlyList<ITweak> CreateTweaks(IEnumerable<string>? groupIds = null)
    {
        var wanted = groupIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return whitelist.Groups
            .Where(group => wanted is null || wanted.Contains(group.Id))
            .Select(ITweak (group) => new CleanupTweak(this, group))
            .ToList();
    }

    internal CleanupGroupScan ScanGroup(CleanupGroup group, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        var rejected = new List<string>();
        var count = 0;
        var bytes = 0L;

        foreach (var entry in EnumerateGroup(group, missing, rejected, cancellationToken))
        {
            count++;
            bytes += entry.SizeBytes;
        }

        return new CleanupGroupScan(group.Id, group.NameEn, group.NameAr, count, bytes, missing, rejected);
    }

    /// <summary>
    /// Walks a group's whitelisted paths. A path that does not exist is normal; a path the guard
    /// refuses is recorded and skipped rather than crashing the scan.
    /// </summary>
    internal IEnumerable<FileEntry> EnumerateGroup(
        CleanupGroup group,
        List<string> missing,
        List<string> rejected,
        CancellationToken cancellationToken)
    {
        foreach (var whitelistPath in group.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string resolved;
            try
            {
                resolved = CleanupWhitelist.Resolve(whitelistPath, environment);
            }
            catch (InvalidOperationException ex)
            {
                rejected.Add($"{whitelistPath}: {ex.Message}");
                continue;
            }

            if (!files.DirectoryExists(resolved))
            {
                missing.Add(resolved);
                continue;
            }

            foreach (var entry in files.EnumerateFiles(resolved, group.Recursive))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return entry;
            }
        }
    }

    /// <summary>Deletes what a group holds, leaving locked files alone.</summary>
    internal CleanupApplyDetail DeleteGroup(CleanupGroup group, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var freed = 0L;
        var locked = 0;

        // Materialised first: deleting while enumerating the same folder is asking for trouble.
        var entries = EnumerateGroup(group, [], [], cancellationToken).ToList();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                files.DeleteFile(entry.FullPath);
                deleted++;
                freed += entry.SizeBytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use, or protected. Expected on every real PC — never a reason to stop.
                locked++;
            }
        }

        return new CleanupApplyDetail(group.Id, deleted, freed, locked);
    }

    /// <summary>The value the log stores for a group before cleaning: what was there.</summary>
    internal static string DescribeState(int fileCount, long totalBytes)
        => string.Create(CultureInfo.InvariantCulture, $"files={fileCount};bytes={totalBytes}");
}
