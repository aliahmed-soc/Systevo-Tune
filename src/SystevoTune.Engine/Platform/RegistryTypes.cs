using System.Globalization;

namespace SystevoTune.Engine.Platform;

/// <summary>Registry root keys the engine is allowed to touch.</summary>
public enum RegistryRoot
{
    /// <summary>HKEY_LOCAL_MACHINE.</summary>
    LocalMachine,

    /// <summary>HKEY_CURRENT_USER.</summary>
    CurrentUser,
}

/// <summary>The subset of registry value types the engine reads and writes.</summary>
public enum RegistryValueType
{
    /// <summary>REG_SZ.</summary>
    String,

    /// <summary>REG_EXPAND_SZ.</summary>
    ExpandString,

    /// <summary>REG_DWORD.</summary>
    Dword,

    /// <summary>REG_QWORD.</summary>
    Qword,

    /// <summary>REG_BINARY. Data is carried as an uppercase hex string.</summary>
    Binary,
}

/// <summary>
/// A registry value's type and data. Data is always carried as text so it can round-trip
/// through the change log, which stores old and new values as strings.
/// </summary>
public sealed record RegistryValue(RegistryValueType Type, string Data)
{
    /// <summary>A REG_DWORD holding <paramref name="value"/>.</summary>
    public static RegistryValue Dword(int value)
        => new(RegistryValueType.Dword, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A REG_SZ holding <paramref name="value"/>.</summary>
    public static RegistryValue Text(string value) => new(RegistryValueType.String, value);

    /// <summary>A REG_BINARY holding <paramref name="bytes"/>.</summary>
    public static RegistryValue Binary(ReadOnlySpan<byte> bytes)
        => new(RegistryValueType.Binary, Convert.ToHexString(bytes));

    /// <summary>The bytes of a REG_BINARY value.</summary>
    /// <exception cref="InvalidOperationException">This value is not binary.</exception>
    public byte[] ToBytes() => Type is RegistryValueType.Binary
        ? Convert.FromHexString(Data)
        : throw new InvalidOperationException($"A {Type} value has no bytes.");

    /// <summary>
    /// Encodes type and data into the single string the change log stores, e.g. <c>Dword:1</c>.
    /// </summary>
    public string ToLogValue() => $"{Type}:{Data}";

    /// <summary>
    /// Reads back <see cref="ToLogValue"/>. A <c>null</c> log value means the value did not exist.
    /// </summary>
    public static RegistryValue? FromLogValue(string? logValue)
    {
        if (logValue is null)
        {
            return null;
        }

        var separator = logValue.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || !Enum.TryParse<RegistryValueType>(logValue[..separator], out var type))
        {
            throw new FormatException($"'{logValue}' is not a registry value the log can read back.");
        }

        return new RegistryValue(type, logValue[(separator + 1)..]);
    }
}

/// <summary>Points at one registry value: root, key path, and value name.</summary>
public sealed record RegistryValueRef(RegistryRoot Root, string KeyPath, string ValueName)
{
    private const string NameSeparator = "::";

    /// <summary>
    /// The change log's <c>target</c> string, e.g. <c>HKLM\SOFTWARE\Foo::Bar</c>.
    /// <c>::</c> separates the value name so the key path may contain backslashes.
    /// </summary>
    public override string ToString() => $"{ShortRoot(Root)}\\{KeyPath}{NameSeparator}{ValueName}";

    /// <summary>Reads back <see cref="ToString"/>.</summary>
    public static RegistryValueRef Parse(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var nameAt = target.IndexOf(NameSeparator, StringComparison.Ordinal);
        var rootAt = target.IndexOf('\\', StringComparison.Ordinal);
        if (nameAt <= 0 || rootAt <= 0 || rootAt > nameAt)
        {
            throw new FormatException($"'{target}' is not a registry target.");
        }

        var root = target[..rootAt] switch
        {
            "HKLM" => RegistryRoot.LocalMachine,
            "HKCU" => RegistryRoot.CurrentUser,
            _ => throw new FormatException($"'{target}' names a registry root the engine does not use."),
        };

        return new RegistryValueRef(root, target[(rootAt + 1)..nameAt], target[(nameAt + NameSeparator.Length)..]);
    }

    private static string ShortRoot(RegistryRoot root) => root switch
    {
        RegistryRoot.LocalMachine => "HKLM",
        RegistryRoot.CurrentUser => "HKCU",
        _ => throw new ArgumentOutOfRangeException(nameof(root), root, "Unknown registry root."),
    };
}

/// <summary>
/// Every registry read and write the engine performs. Real implementation talks to Windows;
/// tests use a fake. No engine code may touch <c>Microsoft.Win32.Registry</c> directly.
/// </summary>
public interface IRegistryService
{
    /// <summary>Whether the key exists.</summary>
    bool KeyExists(RegistryRoot root, string keyPath);

    /// <summary>Names of the values under a key. Empty if the key does not exist.</summary>
    IReadOnlyList<string> GetValueNames(RegistryRoot root, string keyPath);

    /// <summary>The value, or <c>null</c> if the key or the value does not exist.</summary>
    RegistryValue? GetValue(RegistryValueRef reference);

    /// <summary>Writes the value, creating the key if needed.</summary>
    void SetValue(RegistryValueRef reference, RegistryValue value);

    /// <summary>Deletes the value. Does nothing if it is already gone. Never deletes the key.</summary>
    void DeleteValue(RegistryValueRef reference);
}
