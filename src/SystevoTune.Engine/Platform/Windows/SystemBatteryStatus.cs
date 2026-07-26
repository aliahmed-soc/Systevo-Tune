using System.Runtime.InteropServices;

namespace SystevoTune.Engine.Platform.Windows;

/// <summary>
/// The real mains/battery state, from kernel32's <c>GetSystemPowerStatus</c>.
/// </summary>
/// <remarks>
/// Read-only: it asks Windows a question and changes nothing. Never used by unit tests.
/// UNVERIFIED — the struct layout and flag values are listed in the windows-verified-paths skill.
/// </remarks>
public sealed class SystemBatteryStatus : IBatteryStatus
{
    private const byte AcOffline = 0;
    private const byte AcOnline = 1;
    private const byte NoSystemBattery = 128;

    /// <inheritdoc />
    public BatteryState Current
    {
        get
        {
            // A PC that will not answer is not a reason to fail an apply run — the caller just
            // loses the battery warning.
            if (!GetSystemPowerStatus(out var status))
            {
                return BatteryState.Unknown;
            }

            if ((status.BatteryFlag & NoSystemBattery) != 0)
            {
                return BatteryState.NoBattery;
            }

            return status.AcLineStatus switch
            {
                AcOffline => BatteryState.OnBattery,
                AcOnline => BatteryState.PluggedIn,
                _ => BatteryState.Unknown,
            };
        }
    }

    // DllImport rather than LibraryImport: the source generator emits unsafe code, and enabling
    // unsafe across the whole engine for one read-only call is the wrong trade.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatusRaw status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatusRaw
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
