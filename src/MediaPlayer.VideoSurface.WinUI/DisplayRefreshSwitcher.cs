using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MediaPlayer.VideoSurface.WinUI;

/// <summary>
/// A refresh change that was applied, with everything needed to undo it on the
/// same monitor even after the window has moved somewhere else.
/// </summary>
public sealed record RefreshChange(string DeviceName, int PreviousHertz, int Hertz);

/// <summary>
/// Temporary same-resolution refresh change via <c>ChangeDisplaySettingsEx</c>
/// with <c>CDS_FULLSCREEN</c>: the mode is dynamic, never written to the
/// registry, and Windows reverts it when the process exits (crash included).
/// Restores are keyed by the GDI device name captured at set time, not by the
/// window's current monitor.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DisplayRefreshSwitcher
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EnumCurrentSettings = -1;
    private const uint DmBitsPerPel = 0x00040000;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint DmDisplayFlags = 0x00200000;
    private const uint DmDisplayFrequency = 0x00400000;
    private const uint DmInterlaced = 0x00000002;
    private const uint CdsFullscreen = 0x00000004;
    private const uint CdsTest = 0x00000002;
    private const int DispChangeSuccessful = 0;

    private static readonly object ExitGate = new();
    private static readonly Dictionary<string, RefreshChange> Outstanding = new(StringComparer.OrdinalIgnoreCase);
    private static bool _exitHooked;

    public static string? DeviceName(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0)
        {
            return null;
        }

        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return null;
        }

        MonitorInfoEx info = new() { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        return GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
    }

    public static int CurrentHertz(nint hwnd) => CurrentHertz(DeviceName(hwnd));

    public static int CurrentHertz(string? device)
    {
        if (string.IsNullOrWhiteSpace(device))
        {
            return 0;
        }

        DevMode mode = NewMode();
        return EnumDisplaySettings(device, EnumCurrentSettings, ref mode) ? (int)mode.dmDisplayFrequency : 0;
    }

    public static IReadOnlyList<int> AvailableHertz(nint hwnd) => AvailableHertz(DeviceName(hwnd));

    public static IReadOnlyList<int> AvailableHertz(string? device)
    {
        if (string.IsNullOrWhiteSpace(device))
        {
            return [];
        }

        DevMode current = NewMode();
        if (!EnumDisplaySettings(device, EnumCurrentSettings, ref current))
        {
            return [];
        }

        HashSet<int> rates = [];
        for (int index = 0; ; index++)
        {
            DevMode mode = NewMode();
            if (!EnumDisplaySettings(device, index, ref mode))
            {
                break;
            }

            // TVs expose 1080i modes at the same size; an interlaced switch is never wanted.
            if (mode.dmPelsWidth == current.dmPelsWidth
                && mode.dmPelsHeight == current.dmPelsHeight
                && mode.dmBitsPerPel == current.dmBitsPerPel
                && (mode.dmDisplayFlags & DmInterlaced) == 0
                && mode.dmDisplayFrequency is > 10 and < 360)
            {
                rates.Add((int)mode.dmDisplayFrequency);
            }
        }

        return rates.OrderBy(hz => hz).ToArray();
    }

    /// <summary>Legacy entry point; prefer the overload that returns the <see cref="RefreshChange"/>.</summary>
    public static bool TrySetHertz(nint hwnd, int hertz) => TrySetHertz(hwnd, hertz, out _);

    public static bool TrySetHertz(nint hwnd, int hertz, out RefreshChange? change)
    {
        change = null;
        string? device = DeviceName(hwnd);
        if (string.IsNullOrWhiteSpace(device) || hertz is < 20 or > 360)
        {
            return false;
        }

        int previous = CurrentHertz(device);
        if (previous <= 0)
        {
            return false;
        }

        if (!ApplyHertz(device, hertz))
        {
            return false;
        }

        change = new RefreshChange(device, previous, hertz);
        lock (ExitGate)
        {
            Outstanding[device] = change;
            HookExit();
        }

        return true;
    }

    /// <summary>Undo a previous <see cref="TrySetHertz(nint,int,out RefreshChange?)"/> on its own monitor.</summary>
    public static bool TryRestore(RefreshChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (ExitGate)
        {
            Outstanding.Remove(change.DeviceName);
        }

        if (CurrentHertz(change.DeviceName) == change.PreviousHertz)
        {
            return true;
        }

        return ApplyHertz(change.DeviceName, change.PreviousHertz);
    }

    private static bool ApplyHertz(string device, int hertz)
    {
        DevMode mode = NewMode();
        if (!EnumDisplaySettings(device, EnumCurrentSettings, ref mode))
        {
            return false;
        }

        // Some drivers reject a DEVMODE that only names the frequency; carry the
        // current size and depth so the request is a complete mode.
        mode.dmFields = DmPelsWidth | DmPelsHeight | DmBitsPerPel | DmDisplayFrequency | DmDisplayFlags;
        mode.dmDisplayFlags &= ~DmInterlaced;
        mode.dmDisplayFrequency = (uint)hertz;
        if (ChangeDisplaySettingsEx(device, ref mode, 0, CdsTest, 0) != DispChangeSuccessful)
        {
            return false;
        }

        return ChangeDisplaySettingsEx(device, ref mode, 0, CdsFullscreen, 0) == DispChangeSuccessful;
    }

    private static void HookExit()
    {
        if (_exitHooked)
        {
            return;
        }

        _exitHooked = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreAllOutstanding();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreAllOutstanding();
    }

    private static void RestoreAllOutstanding()
    {
        RefreshChange[] pending;
        lock (ExitGate)
        {
            pending = [.. Outstanding.Values];
            Outstanding.Clear();
        }

        foreach (RefreshChange change in pending)
        {
            try
            {
                ApplyHertz(change.DeviceName, change.PreviousHertz);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Exit path: nothing else to do.
            }
        }
    }

    private static DevMode NewMode()
    {
        DevMode mode = new();
        mode.dmSize = (ushort)Marshal.SizeOf<DevMode>();
        return mode;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string deviceName,
        ref DevMode devMode,
        nint hwnd,
        uint flags,
        nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }
}
