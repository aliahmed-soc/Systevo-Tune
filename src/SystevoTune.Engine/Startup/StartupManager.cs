using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Tweaks;
using SystevoTune.Engine.Tweaks.Registry;

namespace SystevoTune.Engine.Startup;

/// <summary>
/// Lists what starts with Windows and switches items off or back on (doc 3.2).
/// </summary>
/// <remarks>
/// Nothing is ever deleted. Disabling writes only the <c>StartupApproved</c> flag — the same
/// mechanism Task Manager uses — so the Run value and the shortcut stay exactly where they were
/// and Windows' own UI shows the item as disabled rather than missing.
/// <para>
/// Because a disable is just a registry write, its records carry the <c>Registry</c> module and
/// are put back by the already-tested <see cref="RegistryUndoHandler"/>.
/// </para>
/// </remarks>
public sealed class StartupManager(
    StartupLocationCatalog catalog,
    IRegistryService registry,
    IFileSystemService files,
    IEnvironmentPaths environment,
    TimeProvider? timeProvider = null)
{
    /// <summary>
    /// A StartupApproved value is 12 bytes: a 4-byte flag DWORD, then an 8-byte FILETIME holding
    /// when the item was disabled. Enabled entries carry a zero FILETIME.
    /// </summary>
    private const int ApprovedValueLength = 12;

    private const byte EnabledFlag = 0x02;
    private const byte DisabledFlag = 0x03;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Everything that starts with Windows, across every whitelisted location.</summary>
    public IReadOnlyList<StartupItem> List(CancellationToken cancellationToken = default)
    {
        var items = new List<StartupItem>();

        foreach (var location in catalog.Locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.AddRange(location.Kind is StartupKind.RegistryRun
                ? ListRunItems(location)
                : ListFolderItems(location));
        }

        return items;
    }

    /// <summary>A tweak that switches one item to <paramref name="target"/>.</summary>
    public ITweak CreateTweak(StartupItem item, StartupState target)
    {
        ArgumentNullException.ThrowIfNull(item);

        var location = catalog.Locations.FirstOrDefault(candidate => candidate.Id == item.LocationId)
            ?? throw new InvalidOperationException($"'{item.LocationId}' is not a startup location.");

        return new StartupItemTweak(this, location, item, target);
    }

    private IEnumerable<StartupItem> ListRunItems(StartupLocation location)
    {
        var (root, key) = location.RunKey();

        foreach (var name in registry.GetValueNames(root, key))
        {
            var command = registry.GetValue(new RegistryValueRef(root, key, name))?.Data ?? string.Empty;
            yield return new StartupItem(name, command, location.Id, location.Kind, ReadState(location, name));
        }
    }

    private IEnumerable<StartupItem> ListFolderItems(StartupLocation location)
    {
        var folder = location.ResolveFolder(environment);
        if (!files.DirectoryExists(folder))
        {
            yield break;
        }

        foreach (var file in files.EnumerateFiles(folder, recursive: false))
        {
            var name = Path.GetFileName(file.FullPath);

            // Windows uses the shortcut's file name, extension included, as the approval key.
            yield return new StartupItem(name, file.FullPath, location.Id, location.Kind, ReadState(location, name));
        }
    }

    /// <summary>
    /// Reads the approval flag. A missing value means Windows has never been told otherwise,
    /// which is enabled.
    /// </summary>
    /// <remarks>
    /// Known flag bytes are <c>0x02</c> and <c>0x06</c> for enabled and <c>0x03</c> for disabled.
    /// The low bit separates them, and testing it rather than matching the three known values
    /// keeps an unknown-but-even flag on the safe side: reported as enabled, so the engine offers
    /// to disable it rather than silently believing it is already off.
    /// </remarks>
    internal StartupState ReadState(StartupLocation location, string itemName)
    {
        var value = registry.GetValue(location.ApprovedRef(itemName));
        if (value is null || value.Type is not RegistryValueType.Binary)
        {
            return StartupState.Enabled;
        }

        var bytes = value.ToBytes();
        return bytes.Length > 0 && (bytes[0] & 0x01) != 0 ? StartupState.Disabled : StartupState.Enabled;
    }

    /// <summary>
    /// Builds the approval value for a state: the flag, then the FILETIME Windows uses to record
    /// when an item was switched off. Enabling writes a zero FILETIME, because there is no
    /// disable time to record.
    /// </summary>
    /// <remarks>
    /// An earlier version carried the existing timestamp across, which was wrong — it would have
    /// stamped a re-disabled item with the time it was disabled the first time.
    /// </remarks>
    internal static RegistryValue BuildApprovedValue(StartupState target, DateTimeOffset now)
    {
        var bytes = new byte[ApprovedValueLength];
        bytes[0] = target is StartupState.Disabled ? DisabledFlag : EnabledFlag;

        if (target is StartupState.Disabled)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(4), now.ToFileTime());
        }

        return RegistryValue.Binary(bytes);
    }

    /// <summary>The approval value for a state, stamped with the current time.</summary>
    internal RegistryValue BuildApprovedValue(StartupState target)
        => BuildApprovedValue(target, _time.GetLocalNow());

    internal RegistryValue? ReadApproved(StartupLocation location, string itemName)
        => registry.GetValue(location.ApprovedRef(itemName));

    internal void WriteApproved(RegistryValueRef reference, RegistryValue value) => registry.SetValue(reference, value);
}

/// <summary>Switches one startup item off or on.</summary>
public sealed class StartupItemTweak(
    StartupManager manager,
    StartupLocation location,
    StartupItem item,
    StartupState target) : ITweak
{
    /// <inheritdoc />
    public string Id { get; } = "startup." + item.Id;

    /// <inheritdoc />
    public string Name { get; } = item.Name;

    /// <summary>Registry, so the tested registry undo handler puts it back.</summary>
    public string Module => RegistryTweak.ModuleName;

    /// <inheritdoc />
    public Task<TweakPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var current = manager.ReadState(location, item.Name);
        if (current == target)
        {
            return Task.FromResult(TweakPlan.AlreadyApplied(Id, Name,
                $"{item.Name} is already {Describe(target)}."));
        }

        var reference = location.ApprovedRef(item.Name);
        var existing = manager.ReadApproved(location, item.Name);

        var change = new PlannedChange(
            Module,
            RegistryTweak.SetAction,
            reference.ToString(),
            existing?.ToLogValue(),
            manager.BuildApprovedValue(target).ToLogValue(),
            $"Startup: {Describe(target)} {item.Name}. The entry stays in place and can be switched back.");

        return Task.FromResult(TweakPlan.Ready(Id, Name, [change]));
    }

    /// <inheritdoc />
    public Task ApplyChangeAsync(PlannedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var reference = RegistryValueRef.Parse(change.Target);
        var value = RegistryValue.FromLogValue(change.NewValue)
            ?? throw new InvalidOperationException($"'{change.Target}' has no new value to write.");

        manager.WriteApproved(reference, value);
        return Task.CompletedTask;
    }

    private static string Describe(StartupState state) => state is StartupState.Disabled ? "disabled" : "enabled";
}
