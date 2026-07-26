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
    IEnvironmentPaths environment)
{
    /// <summary>Length of a StartupApproved value. Byte 0 is the flag; the rest is a timestamp.</summary>
    private const int ApprovedValueLength = 12;

    private const byte EnabledFlag = 0x02;
    private const byte DisabledFlag = 0x03;

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
    /// Builds the approval value for a state, keeping the timestamp bytes of whatever is already
    /// there so switching an item off and on again does not lose Windows' own bookkeeping.
    /// </summary>
    internal static RegistryValue BuildApprovedValue(RegistryValue? current, StartupState target)
    {
        var bytes = current?.Type is RegistryValueType.Binary && current.ToBytes().Length >= ApprovedValueLength
            ? current.ToBytes()
            : new byte[ApprovedValueLength];

        bytes[0] = target is StartupState.Disabled ? DisabledFlag : EnabledFlag;
        return RegistryValue.Binary(bytes);
    }

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
            StartupManager.BuildApprovedValue(existing, target).ToLogValue(),
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
