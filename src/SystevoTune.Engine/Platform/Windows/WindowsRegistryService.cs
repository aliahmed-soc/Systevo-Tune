using System.Globalization;
using Microsoft.Win32;

namespace SystevoTune.Engine.Platform.Windows;

/// <summary>
/// The real registry. Thin on purpose — it only translates engine types to Win32 calls, so the
/// logic that decides what to write lives in tested code and this class has nothing to get wrong.
/// </summary>
/// <remarks>Never used by unit tests. Exercised only in the VM procedure of doc 07.</remarks>
public sealed class WindowsRegistryService : IRegistryService
{
    /// <inheritdoc />
    public bool KeyExists(RegistryRoot root, string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        using var key = OpenRoot(root).OpenSubKey(keyPath, writable: false);
        return key is not null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        using var key = OpenRoot(root).OpenSubKey(keyPath, writable: false);
        return key is null ? [] : key.GetValueNames();
    }

    /// <inheritdoc />
    public RegistryValue? GetValue(RegistryValueRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        using var key = OpenRoot(reference.Root).OpenSubKey(reference.KeyPath, writable: false);
        var raw = key?.GetValue(reference.ValueName, defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);

        if (key is null || raw is null)
        {
            return null;
        }

        return key.GetValueKind(reference.ValueName) switch
        {
            RegistryValueKind.String => new RegistryValue(RegistryValueType.String, Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty),
            RegistryValueKind.ExpandString => new RegistryValue(RegistryValueType.ExpandString, Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty),
            RegistryValueKind.DWord => new RegistryValue(RegistryValueType.Dword, Convert.ToInt32(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => new RegistryValue(RegistryValueType.Qword, Convert.ToInt64(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.Binary => RegistryValue.Binary((byte[])raw),
            var kind => throw new NotSupportedException(
                $"'{reference}' holds a {kind} value, which the engine does not read or write."),
        };
    }

    /// <inheritdoc />
    public void SetValue(RegistryValueRef reference, RegistryValue value)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(value);

        using var key = OpenRoot(reference.Root).CreateSubKey(reference.KeyPath, writable: true)
            ?? throw new InvalidOperationException($"Could not open '{reference.KeyPath}' for writing.");

        switch (value.Type)
        {
            case RegistryValueType.String:
                key.SetValue(reference.ValueName, value.Data, RegistryValueKind.String);
                break;
            case RegistryValueType.ExpandString:
                key.SetValue(reference.ValueName, value.Data, RegistryValueKind.ExpandString);
                break;
            case RegistryValueType.Dword:
                key.SetValue(reference.ValueName, int.Parse(value.Data, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
                break;
            case RegistryValueType.Qword:
                key.SetValue(reference.ValueName, long.Parse(value.Data, CultureInfo.InvariantCulture), RegistryValueKind.QWord);
                break;
            case RegistryValueType.Binary:
                key.SetValue(reference.ValueName, value.ToBytes(), RegistryValueKind.Binary);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.Type, "Unknown registry value type.");
        }
    }

    /// <inheritdoc />
    public void DeleteValue(RegistryValueRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        using var key = OpenRoot(reference.Root).OpenSubKey(reference.KeyPath, writable: true);
        key?.DeleteValue(reference.ValueName, throwOnMissingValue: false);
    }

    private static RegistryKey OpenRoot(RegistryRoot root) => root switch
    {
        RegistryRoot.LocalMachine => Registry.LocalMachine,
        RegistryRoot.CurrentUser => Registry.CurrentUser,
        _ => throw new ArgumentOutOfRangeException(nameof(root), root, "Unknown registry root."),
    };
}
