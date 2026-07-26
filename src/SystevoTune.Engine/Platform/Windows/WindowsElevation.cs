using System.Security.Principal;

namespace SystevoTune.Engine.Platform.Windows;

/// <summary>Whether the process is running as administrator.</summary>
public interface IElevation
{
    /// <summary>True when the process can write HKLM and change services.</summary>
    bool IsElevated { get; }
}

/// <summary>
/// The real check. Read-only: it asks Windows about the current token and changes nothing.
/// </summary>
public sealed class WindowsElevation : IElevation
{
    /// <inheritdoc />
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
