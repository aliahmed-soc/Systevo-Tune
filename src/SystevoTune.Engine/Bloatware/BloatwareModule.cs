using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;
using SystevoTune.Engine.Tweaks;

namespace SystevoTune.Engine.Bloatware;

/// <summary>Removing preloaded apps the user ticked (doc 3.8).</summary>
public sealed class BloatwareModule(BloatwareWhitelist whitelist, IAppPackageService packages)
{
    /// <summary>Module name on every bloatware change record.</summary>
    public const string ModuleName = "Bloatware";

    /// <summary>Action on every bloatware change record.</summary>
    public const string RemoveAction = "RemoveAppPackage";

    /// <summary>
    /// One tweak per approved package that is actually installed. An unapproved entry produces
    /// nothing, so the shipped whitelist removes nothing at all.
    /// </summary>
    public async Task<IReadOnlyList<ITweak>> CreateTweaksAsync(
        IEnumerable<string>? packageNames = null,
        CancellationToken cancellationToken = default)
    {
        var wanted = packageNames?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installed = await packages.ListAsync(cancellationToken).ConfigureAwait(false);

        return whitelist.Approved
            .Where(entry => wanted is null || wanted.Contains(entry.Name))
            .Select(entry => (Entry: entry, Package: installed.FirstOrDefault(
                candidate => string.Equals(candidate.Name, entry.Name, StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.Package is not null)
            .Select(ITweak (pair) => new BloatwareTweak(packages, pair.Entry, pair.Package!))
            .ToList();
    }

    /// <summary>Approved packages that are installed right now, for the tick-list screen.</summary>
    public async Task<IReadOnlyList<BloatwareEntry>> ListRemovableAsync(CancellationToken cancellationToken = default)
    {
        var installed = await packages.ListAsync(cancellationToken).ConfigureAwait(false);

        return whitelist.Approved
            .Where(entry => installed.Any(
                candidate => string.Equals(candidate.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}

/// <summary>Removing one package.</summary>
public sealed class BloatwareTweak(IAppPackageService packages, BloatwareEntry entry, AppPackage package) : ITweak
{
    /// <inheritdoc />
    public string Id { get; } = "bloatware." + entry.Name;

    /// <inheritdoc />
    public string Name { get; } = entry.NameEn;

    /// <inheritdoc />
    public string Module => BloatwareModule.ModuleName;

    /// <inheritdoc />
    public Task<TweakPlan> PlanAsync(CancellationToken cancellationToken)
    {
        // The install location goes in the old value because it is the only thing that gives undo
        // a chance: re-registering needs the original files.
        var change = new PlannedChange(
            Module,
            BloatwareModule.RemoveAction,
            package.Name,
            package.FullName,
            null,
            $"Remove {entry.NameEn}. {entry.WhyEn} "
            + "Undo will try to put it back, but that usually needs the Microsoft Store.");

        return Task.FromResult(TweakPlan.Ready(Id, Name, [change]));
    }

    /// <inheritdoc />
    public Task ApplyChangeAsync(PlannedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (BloatwareWhitelist.IsForbidden(package.Name))
        {
            // Belt and braces. The whitelist already refuses these at load time.
            throw new InvalidOperationException($"'{package.Name}' is part of Windows and may not be removed.");
        }

        return packages.RemoveAsync(package, cancellationToken);
    }
}

/// <summary>
/// Tries to put a removed app back.
/// </summary>
/// <remarks>
/// The one undo in this engine that is honestly expected to fail. Removal usually takes the
/// package files with it, and after that only the Store can reinstall. Doc 01 rules out
/// overselling, so a failure says plainly where to get the app rather than hiding behind
/// "undo failed".
/// </remarks>
public sealed class BloatwareUndoHandler(IAppPackageService packages) : IUndoHandler
{
    /// <inheritdoc />
    public string Module => BloatwareModule.ModuleName;

    /// <inheritdoc />
    public async Task UndoAsync(ChangeRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.OldValue))
        {
            throw new InvalidOperationException(
                $"'{record.Target}' was removed but its package identity was not recorded, so it cannot be put back. "
                + "Reinstall it from the Microsoft Store.");
        }

        var package = new AppPackage(record.Target, record.OldValue, string.Empty);

        if (!await packages.TryReinstallAsync(package, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"'{record.Target}' could not be reinstalled automatically — removing an app usually deletes the files "
                + "needed to put it back. Reinstall it from the Microsoft Store.");
        }
    }
}
