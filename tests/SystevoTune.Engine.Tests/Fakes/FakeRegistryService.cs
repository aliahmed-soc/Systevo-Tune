using SystevoTune.Engine.Platform;

namespace SystevoTune.Engine.Tests.Fakes;

/// <summary>
/// An in-memory registry. The only registry any unit test is allowed to touch.
/// </summary>
internal sealed class FakeRegistryService : IRegistryService
{
    private readonly Dictionary<string, RegistryValue> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Value refs whose write or delete throws, for exercising failure paths.</summary>
    public HashSet<string> FailingTargets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every write, in order, as <c>target = logValue</c>.</summary>
    public List<string> Writes { get; } = [];

    /// <summary>Seeds a value and its key, as if Windows already had it.</summary>
    public FakeRegistryService With(RegistryValueRef reference, RegistryValue value)
    {
        _keys.Add(reference.KeyPath);
        _values[reference.ToString()] = value;
        return this;
    }

    /// <summary>Seeds a key that exists but holds none of the values under test.</summary>
    public FakeRegistryService WithKey(string keyPath)
    {
        _keys.Add(keyPath);
        return this;
    }

    /// <inheritdoc />
    public bool KeyExists(RegistryRoot root, string keyPath) => _keys.Contains(keyPath);

    /// <inheritdoc />
    public RegistryValue? GetValue(RegistryValueRef reference)
        => _values.GetValueOrDefault(reference.ToString());

    /// <inheritdoc />
    public void SetValue(RegistryValueRef reference, RegistryValue value)
    {
        Guard(reference);

        _keys.Add(reference.KeyPath);
        _values[reference.ToString()] = value;
        Writes.Add($"{reference} = {value.ToLogValue()}");
    }

    /// <inheritdoc />
    public void DeleteValue(RegistryValueRef reference)
    {
        Guard(reference);

        _values.Remove(reference.ToString());
        Writes.Add($"{reference} = <deleted>");
    }

    private void Guard(RegistryValueRef reference)
    {
        if (FailingTargets.Contains(reference.ToString()))
        {
            throw new UnauthorizedAccessException($"'{reference}' is not writable.");
        }
    }
}
