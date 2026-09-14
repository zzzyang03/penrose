using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MediaPlayer.VideoSurface.WinUI;

/// <summary>
/// Window's current <c>IDXGIOutput6::GetDesc1</c> plus DisplayConfig SDR white.
/// Inject <c>target-peak</c> from this display, not max-of-outputs.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DxgiDisplay
{
    private static readonly Guid DxgiFactory1Iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid DxgiOutput6Iid = new("068346e8-aaec-4b84-add7-137f513f77a1");

    private const uint QdcOnlyActivePaths = 2;
    private const uint MonitorDefaultToNearest = 2;
    private const int DeviceInfoGetSourceName = 1;
    private const int DeviceInfoGetSdrWhiteLevel = 11;

    public static DisplayColorCapabilities? ForWindow(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0)
        {
            return null;
        }

        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        return monitor == 0 ? null : ForMonitor(monitor);
    }

    public static DisplayColorCapabilities? ForMonitor(nint monitor)
    {
        if (!OperatingSystem.IsWindows() || monitor == 0)
        {
            return null;
        }

        foreach (OutputRow row in EnumerateOutputs())
        {
            if (row.Monitor != monitor)
            {
                continue;
            }

            double sdrWhite = ReadSdrWhiteNits(row.DeviceName);
            return new DisplayColorCapabilities(
                row.AdapterLuid,
                ColorMode(row.ColorSpace),
                ColorSpaceName(row.ColorSpace),
                row.MaxLuminance,
                row.MinLuminance,
                row.MaxFullFrameLuminance,
                ContainerPrimaries: null,
                EffectiveGamut: null,
                SdrWhiteLevel: sdrWhite);
        }

        return null;
    }

    public static double RefreshRateHz(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0)
        {
            return 0;
        }

        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return 0;
        }

        foreach (OutputRow row in EnumerateOutputs())
        {
            if (row.Monitor != monitor)
            {
                continue;
            }

            return ReadRefreshHz(row.DeviceName);
        }

        return 0;
    }

    private static ActiveColorMode ColorMode(int colorSpace) =>
        colorSpace switch
        {
            1 or 12 or 13 or 14 or 16 => ActiveColorMode.Hdr,
            17 => ActiveColorMode.WindowsWideColor,
            _ => ActiveColorMode.Sdr,
        };

    private static string ColorSpaceName(int value) => value switch
    {
        0 => "RGB_FULL_G22_NONE_P709",
        1 => "RGB_FULL_G10_NONE_P709",
        12 => "RGB_FULL_G2084_NONE_P2020",
        14 => "RGB_STUDIO_G2084_NONE_P2020",
        17 => "RGB_FULL_G22_NONE_P2020",
        _ => "cs-" + value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static List<OutputRow> EnumerateOutputs()
    {
        List<OutputRow> rows = [];
        Guid iid = DxgiFactory1Iid;
        int hr = CreateDXGIFactory1(ref iid, out nint factory);
        if (hr < 0 || factory == 0)
        {
            return rows;
        }

        try
        {
            nint factoryVtbl = Marshal.ReadIntPtr(factory);
            EnumAdapters1Delegate enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(
                Marshal.ReadIntPtr(factoryVtbl, 12 * nint.Size));

            for (uint a = 0; a < 16; a++)
            {
                hr = enumAdapters1(factory, a, out nint adapter);
                if (hr < 0 || adapter == 0)
                {
                    break;
                }

                try
                {
                    nint adapterVtbl = Marshal.ReadIntPtr(adapter);
                    GetAdapterDesc1Delegate getAdapter = Marshal.GetDelegateForFunctionPointer<GetAdapterDesc1Delegate>(
                        Marshal.ReadIntPtr(adapterVtbl, 10 * nint.Size));
                    EnumOutputsDelegate enumOutputs = Marshal.GetDelegateForFunctionPointer<EnumOutputsDelegate>(
                        Marshal.ReadIntPtr(adapterVtbl, 7 * nint.Size));
                    long luid = 0;
                    if (getAdapter(adapter, out DxgiAdapterDesc1 adapterDesc) >= 0)
                    {
                        luid = ((long)adapterDesc.AdapterLuidHigh << 32)
                            | adapterDesc.AdapterLuidLow;
                    }

                    for (uint o = 0; o < 8; o++)
                    {
                        hr = enumOutputs(adapter, o, out nint output);
                        if (hr < 0 || output == 0)
                        {
                            break;
                        }

                        nint output6 = 0;
                        try
                        {
                            nint outputVtbl = Marshal.ReadIntPtr(output);
                            QueryInterfaceDelegate qi = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(
                                Marshal.ReadIntPtr(outputVtbl, 0));
                            Guid output6Iid = DxgiOutput6Iid;
                            if (qi(output, ref output6Iid, out output6) < 0 || output6 == 0)
                            {
                                continue;
                            }

                            nint output6Vtbl = Marshal.ReadIntPtr(output6);
                            GetOutputDesc1Delegate getDesc1 = Marshal.GetDelegateForFunctionPointer<GetOutputDesc1Delegate>(
                                Marshal.ReadIntPtr(output6Vtbl, 27 * nint.Size));
                            if (getDesc1(output6, out DxgiOutputDesc1 desc) < 0
                                || desc.AttachedToDesktop == 0
                                || desc.Monitor == 0)
                            {
                                continue;
                            }

                            rows.Add(new OutputRow(
                                desc.DeviceName ?? "",
                                desc.Monitor,
                                luid,
                                desc.ColorSpace,
                                desc.MinLuminance,
                                desc.MaxLuminance,
                                desc.MaxFullFrameLuminance));
                        }
                        finally
                        {
                            ReleaseCom(output);
                            ReleaseCom(output6);
                        }
                    }
                }
                finally
                {
                    ReleaseCom(adapter);
                }
            }
        }
        finally
        {
            ReleaseCom(factory);
        }

        return rows;
    }

    private static double ReadSdrWhiteNits(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return DisplayColorCapabilities.SdrFallbackNits;
        }

        int err = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount);
        if (err != 0)
        {
            return DisplayColorCapabilities.SdrFallbackNits;
        }

        DisplayconfigPathInfo[] paths = new DisplayconfigPathInfo[pathCount];
        DisplayconfigModeInfo[] modes = new DisplayconfigModeInfo[modeCount];
        err = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
        if (err != 0)
        {
            return DisplayColorCapabilities.SdrFallbackNits;
        }

        for (int i = 0; i < pathCount; i++)
        {
            DisplayconfigPathInfo path = paths[i];
            DisplayconfigSourceDeviceName source = new()
            {
                Header = new DisplayconfigDeviceInfoHeader
                {
                    Type = DeviceInfoGetSourceName,
                    Size = (uint)Marshal.SizeOf<DisplayconfigSourceDeviceName>(),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0
                || !string.Equals(source.ViewGdiDeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DisplayconfigSdrWhiteLevel white = new()
            {
                Header = new DisplayconfigDeviceInfoHeader
                {
                    Type = DeviceInfoGetSdrWhiteLevel,
                    Size = (uint)Marshal.SizeOf<DisplayconfigSdrWhiteLevel>(),
                    AdapterId = path.TargetInfo.AdapterId,
                    Id = path.TargetInfo.Id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref white) != 0 || white.SdrWhiteLevel == 0)
            {
                break;
            }

            // DISPLAYCONFIG_SDR_WHITE_LEVEL is in thousandths of 80 nits:
            // 1000 = 80 nits, 2537 ≈ 203 nits. It is not millinits.
            return white.SdrWhiteLevel / 1000.0 * 80.0;
        }

        return DisplayColorCapabilities.SdrFallbackNits;
    }

    private static double ReadRefreshHz(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return 0;
        }

        int err = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount);
        if (err != 0)
        {
            return 0;
        }

        DisplayconfigPathInfo[] paths = new DisplayconfigPathInfo[pathCount];
        DisplayconfigModeInfo[] modes = new DisplayconfigModeInfo[modeCount];
        err = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
        if (err != 0)
        {
            return 0;
        }

        for (int i = 0; i < pathCount; i++)
        {
            DisplayconfigPathInfo path = paths[i];
            DisplayconfigSourceDeviceName source = new()
            {
                Header = new DisplayconfigDeviceInfoHeader
                {
                    Type = DeviceInfoGetSourceName,
                    Size = (uint)Marshal.SizeOf<DisplayconfigSourceDeviceName>(),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0
                || !string.Equals(source.ViewGdiDeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DisplayconfigRational rate = path.TargetInfo.RefreshRate;
            if (rate.Denominator == 0 || rate.Numerator == 0)
            {
                return 0;
            }

            return rate.Numerator / (double)rate.Denominator;
        }

        return 0;
    }

    private static void ReleaseCom(nint punk)
    {
        if (punk == 0)
        {
            return;
        }

        nint vtbl = Marshal.ReadIntPtr(punk);
        ReleaseDelegate release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(
            Marshal.ReadIntPtr(vtbl, 2 * nint.Size));
        _ = release(punk);
    }

    private sealed record OutputRow(
        string DeviceName,
        nint Monitor,
        long AdapterLuid,
        int ColorSpace,
        float MinLuminance,
        float MaxLuminance,
        float MaxFullFrameLuminance);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out nint ppFactory);

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
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayconfigSourceDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayconfigSdrWhiteLevel request);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(nint factory, uint adapter, out nint ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetAdapterDesc1Delegate(nint adapter, out DxgiAdapterDesc1 desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumOutputsDelegate(nint adapter, uint output, out nint ppOutput);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(nint obj, ref Guid iid, out nint ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetOutputDesc1Delegate(nint output, out DxgiOutputDesc1 desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(nint punk);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public ulong DedicatedVideoMemory;
        public ulong DedicatedSystemMemory;
        public ulong SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
    private struct DxgiOutputDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int AttachedToDesktop;
        public int Rotation;
        public nint Monitor;
        public uint BitsPerColor;
        public int ColorSpace;
        public float RedX;
        public float RedY;
        public float GreenX;
        public float GreenY;
        public float BlueX;
        public float BlueY;
        public float WhiteX;
        public float WhiteY;
        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayconfigSourceDeviceName
    {
        public DisplayconfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigSdrWhiteLevel
    {
        public DisplayconfigDeviceInfoHeader Header;
        public uint SdrWhiteLevel;
    }
}
