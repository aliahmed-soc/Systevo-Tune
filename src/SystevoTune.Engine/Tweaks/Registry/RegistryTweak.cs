using SystevoTune.Engine.Platform;
using SystevoTune.Engine.Safety;

namespace SystevoTune.Engine.Tweaks.Registry;

/// <summary>
/// A tweak that writes registry values from the whitelist. Covers visual effects, Game Mode,
/// Game Bar recording, and GPU scheduling (doc 3.5 and 3.6) with one tested mechanism.
/// </summary>
public sealed class RegistryTweak(IRegistryService registry, RegistryTweakEntry entry) : ITweak
{
    /// <summary>Module name on every registry change record.</summary>
    public const string ModuleName = "Registry";

    /// <summary>Action name on every registry change record.</summary>
    public const string SetAction = "SetValue";

    /// <inheritdoc />
    public string Id => entry.Id;

    /// <inheritdoc />
    public string Name => entry.NameEn;

    /// <inheritdoc />
    public string Module => ModuleName;

    /// <inheritdoc />
    public Task<TweakPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var changes = new List<PlannedChange>();

        foreach (var wanted in entry.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reference = wanted.ToRef();
            var current = registry.GetValue(reference);

            // Some settings only exist where the hardware supports them. Writing the value on a
            // PC that never had it would invent a setting rather than change one.
            if (current is null && entry.RequiresExistingValue)
            {
                return Task.FromResult(TweakPlan.NotApplicable(Id, Name,
                    $"{entry.NameEn} is not available on this PC, so nothing was changed."));
            }

            var target = wanted.ToValue();
            if (current == target)
            {
                continue;
            }

            changes.Add(new PlannedChange(
                ModuleName,
                SetAction,
                reference.ToString(),
                current?.ToLogValue(),
                target.ToLogValue(),
                $"{entry.NameEn}: {reference.ValueName} {Describe(current)} to {target.Data}."));
        }

        if (changes.Count == 0)
        {
            return Task.FromResult(TweakPlan.AlreadyApplied(Id, Name, $"{entry.NameEn} is already set."));
        }

        return Task.FromResult(TweakPlan.Ready(Id, Name, changes, entry.RequiresRestart));
    }

    /// <inheritdoc />
    public Task ApplyChangeAsync(PlannedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var reference = RegistryValueRef.Parse(change.Target);
        var value = RegistryValue.FromLogValue(change.NewValue)
            ?? throw new InvalidOperationException($"'{change.Target}' has no new value to write.");

        registry.SetValue(reference, value);
        return Task.CompletedTask;
    }

    private static string Describe(RegistryValue? value) => value is null ? "(not set)" : value.Data;
}

/// <summary>
/// Puts a registry value back. Restores the exact previous value, and removes the value entirely
/// if it did not exist before — doc 7.3 wants the previous state, not a Windows default.
/// </summary>
public sealed class RegistryUndoHandler(IRegistryService registry) : IUndoHandler
{
    /// <inheritdoc />
    public string Module => RegistryTweak.ModuleName;

    /// <inheritdoc />
    public Task UndoAsync(ChangeRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var reference = RegistryValueRef.Parse(record.Target);
        var previous = RegistryValue.FromLogValue(record.OldValue);

        if (previous is null)
        {
            // The value did not exist before the tweak, so putting it back means removing it.
            registry.DeleteValue(reference);
        }
        else
        {
            registry.SetValue(reference, previous);
        }

        return Task.CompletedTask;
    }
}
