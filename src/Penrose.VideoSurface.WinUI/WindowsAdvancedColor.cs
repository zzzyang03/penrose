using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Penrose.VideoSurface.WinUI;

/// <summary>
/// Windows Advanced Color for the window's display. The host may toggle it;
/// the host must restore whatever it enabled.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsAdvancedColor
{
    private const uint QdcOnlyActivePaths = 2;
    private const int DeviceInfoGetSourceName = 1;
    private const int DeviceInfoGetAdvancedColor = 9;
    private const int DeviceInfoSetAdvancedColor = 10;
    // Windows 11 24H2+: distinguishes HDR from WCG / Auto Color Management,
    // which the legacy "advancedColorEnabled" bit no longer does.
    private const int DeviceInfoGetAdvancedColor2 = 15;
    private const int DeviceInfoSetHdrState = 16;
    private const int AdvancedColorModeHdr = 2;

    private static readonly object ExitGate = new();
    private static readonly Dictionary<AdvancedColorTarget, bool> Outstanding = new();
    private static bool _exitHooked;

    public readonly record struct AdvancedColorState(bool Supported, bool Enabled);

    /// <summary>A display target (adapter LUID + target id) captured when HDR was toggled.</summary>
    public readonly record struct AdvancedColorTarget(uint AdapterLow, int AdapterHigh, uint TargetId);

    public static DisplayColorCapabilities? ForWindow(nint hwnd) => DxgiDisplay.ForWindow(hwnd);

    public static AdvancedColorState StateForWindow(nint hwnd)
    {
        if (!TryFindTarget(hwnd, out DisplayconfigPathInfo path))
        {
            return default;
        }

        return StateForPath(path);
    }

    public static bool TryGetTarget(nint hwnd, out AdvancedColorTarget target)
    {
        target = default;
        if (!TryFindTarget(hwnd, out DisplayconfigPathInfo path))
        {
            return false;
        }

        target = ToTarget(path);
        return true;
    }

    public static bool TrySetForWindow(nint hwnd, bool enable) => TrySetForWindow(hwnd, enable, out _);

    /// <summary>
    /// Toggle HDR on the window's current display and hand back the target so
    /// the caller can restore that display even after the window moved.
    /// </summary>
    public static bool TrySetForWindow(nint hwnd, bool enable, out AdvancedColorTarget target)
    {
        target = default;
        if (!TryFindTarget(hwnd, out DisplayconfigPathInfo path))
        {
            return false;
        }

        target = ToTarget(path);
        return TrySetForTarget(target, enable);
    }

    public static bool TrySetForTarget(AdvancedColorTarget target, bool enable)
    {
        DisplayconfigDeviceInfoHeader header = new()
        {
            AdapterId = new Luid { LowPart = target.AdapterLow, HighPart = target.AdapterHigh },
            Id = target.TargetId,
        };

        bool ok;
        DisplayconfigSetHdrState hdr = new()
        {
            Header = header with { Type = DeviceInfoSetHdrState, Size = (uint)Marshal.SizeOf<DisplayconfigSetHdrState>() },
            Value = enable ? 1u : 0u,
        };
        ok = DisplayConfigSetDeviceInfo(ref hdr) == 0;
        if (!ok)
        {
            DisplayconfigSetAdvancedColorState legacy = new()
            {
                Header = header with { Type = DeviceInfoSetAdvancedColor, Size = (uint)Marshal.SizeOf<DisplayconfigSetAdvancedColorState>() },
                Value = enable ? 1u : 0u,
            };
            ok = DisplayConfigSetDeviceInfo(ref legacy) == 0;
        }

        if (ok)
        {
            lock (ExitGate)
            {
                if (enable)
                {
                    // Remember to turn it back off if the process dies before the host does.
                    Outstanding[target] = false;
                    HookExit();
                }
                else
                {
                    Outstanding.Remove(target);
                }
            }
        }

        return ok;
    }

    private static AdvancedColorState StateForPath(DisplayconfigPathInfo path)
    {
        DisplayconfigGetAdvancedColorInfo2 info2 = new()
        {
            Header = new DisplayconfigDeviceInfoHeader
            {
                Type = DeviceInfoGetAdvancedColor2,
                Size = (uint)Marshal.SizeOf<DisplayconfigGetAdvancedColorInfo2>(),
                AdapterId = path.TargetInfo.AdapterId,
                Id = path.TargetInfo.Id,
            },
        };
        if (DisplayConfigGetDeviceInfo(ref info2) == 0)
        {
            // bit 4 highDynamicRangeSupported, bit 5 highDynamicRangeUserEnabled.
            return new AdvancedColorState(
                Supported: (info2.Value & (1u << 4)) != 0,
                Enabled: info2.ActiveColorMode == AdvancedColorModeHdr || (info2.Value & (1u << 5)) != 0);
        }

        DisplayconfigGetAdvancedColorInfo info = ColorInfo(path);
        if (DisplayConfigGetDeviceInfo(ref info) != 0)
        {
            return default;
        }

        return new AdvancedColorState(
            Supported: (info.Value & 1) != 0,
            Enabled: (info.Value & 2) != 0);
    }

    private static AdvancedColorTarget ToTarget(DisplayconfigPathInfo path) =>
        new(path.TargetInfo.AdapterId.LowPart, path.TargetInfo.AdapterId.HighPart, path.TargetInfo.Id);

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
        KeyValuePair<AdvancedColorTarget, bool>[] pending;
        lock (ExitGate)
        {
            pending = [.. Outstanding];
            Outstanding.Clear();
        }

        foreach ((AdvancedColorTarget target, bool state) in pending)
        {
            try
            {
                TrySetForTarget(target, state);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Exit path: nothing else to do.
            }
        }
    }

    public static bool AnyHdrEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        foreach (DisplayconfigPathInfo path in ActivePaths())
        {
            if (StateForPath(path).Enabled)
            {
                return true;
            }
        }

        return false;
    }

    private static DisplayconfigPathInfo[] ActivePaths()
    {
        int err = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount);
        if (err != 0)
        {
            return [];
        }

        DisplayconfigPathInfo[] paths = new DisplayconfigPathInfo[pathCount];
        DisplayconfigModeInfo[] modes = new DisplayconfigModeInfo[modeCount];
        err = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
        if (err != 0)
        {
            return [];
        }

        if (pathCount < paths.Length)
        {
            Array.Resize(ref paths, (int)pathCount);
        }

        return paths;
    }

    private static bool TryFindTarget(nint hwnd, out DisplayconfigPathInfo path)
    {
        path = default;
        string? device = DisplayRefreshSwitcher.DeviceName(hwnd);
        if (string.IsNullOrWhiteSpace(device))
        {
            return false;
        }

        foreach (DisplayconfigPathInfo candidate in ActivePaths())
        {
            DisplayconfigSourceDeviceName source = new()
            {
                Header = new DisplayconfigDeviceInfoHeader
                {
                    Type = DeviceInfoGetSourceName,
                    Size = (uint)Marshal.SizeOf<DisplayconfigSourceDeviceName>(),
                    AdapterId = candidate.SourceInfo.AdapterId,
                    Id = candidate.SourceInfo.Id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0
                || !string.Equals(source.ViewGdiDeviceName, device, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            path = candidate;
            return true;
        }

        return false;
    }

    private static DisplayconfigGetAdvancedColorInfo ColorInfo(DisplayconfigPathInfo path) =>
        new()
        {
            Header = new DisplayconfigDeviceInfoHeader
            {
                Type = DeviceInfoGetAdvancedColor,
                Size = (uint)Marshal.SizeOf<DisplayconfigGetAdvancedColorInfo>(),
                AdapterId = path.TargetInfo.AdapterId,
                Id = path.TargetInfo.Id,
            },
        };

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayconfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayconfigModeInfo[] modeInfoArray,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayconfigGetAdvancedColorInfo request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayconfigSourceDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref DisplayconfigSetAdvancedColorState request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayconfigGetAdvancedColorInfo2 request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref DisplayconfigSetHdrState request);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public int OutputTechnology;
        public int Rotation;
        public int Scaling;
        public DisplayconfigRational RefreshRate;
        public int ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigPathInfo
    {
        public DisplayconfigPathSourceInfo SourceInfo;
        public DisplayconfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigModeInfo
    {
        public int InfoType;
        public uint Id;
        public Luid AdapterId;
        public ulong PixelRate;
        public DisplayconfigRational HSyncFreq;
        public DisplayconfigRational VSyncFreq;
        public uint ActiveWidth;
        public uint ActiveHeight;
        public uint TotalWidth;
        public uint TotalHeight;
        public uint VideoStandard;
        public int ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigDeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigGetAdvancedColorInfo
    {
        public DisplayconfigDeviceInfoHeader Header;
        public uint Value;
        public int ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigSetAdvancedColorState
    {
        public DisplayconfigDeviceInfoHeader Header;
        public uint Value;
    }

    /// <summary>DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 (36 bytes, Windows 11 24H2+).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigGetAdvancedColorInfo2
    {
        public DisplayconfigDeviceInfoHeader Header;
        public uint Value;
        public int ColorEncoding;
        public uint BitsPerColorChannel;
        public int ActiveColorMode;
    }

    /// <summary>DISPLAYCONFIG_SET_HDR_STATE (24 bytes, Windows 11 24H2+).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigSetHdrState
    {
        public DisplayconfigDeviceInfoHeader Header;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayconfigSourceDeviceName
    {
        public DisplayconfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }
}
