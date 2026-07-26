using System.Runtime.InteropServices;
using SystevoTune.Engine.Metrics;

namespace SystevoTune.Engine.Platform.Windows;

/// <summary>
/// Live memory figures from kernel32's <c>GlobalMemoryStatusEx</c>.
/// </summary>
/// <remarks>
/// Read-only: it asks Windows a question and changes nothing. Never used by unit tests.
/// UNVERIFIED — the struct layout is listed in the windows-verified-paths skill.
/// </remarks>
public sealed class WindowsSystemMetrics : ISystemMetrics
{
    /// <inheritdoc />
    public MemoryReading? ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

        // A PC that will not answer costs the user one number, not the run.
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryReading((long)status.TotalPhysical, (long)status.AvailablePhysical)
            : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
