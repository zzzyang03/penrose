using System.Runtime.InteropServices;
using System.Text;

namespace Penrose.VideoSurface.WinUI;

internal static class MpvWin32
{
    private const int SwShow = 5;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int GwlpWndProc = -4;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExTopmost = 0x00000008;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoCopyBits = 0x0100;
    private const uint MonitorDefaultToNearest = 2;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmClose = 0x0010;
    private const uint WmNcDestroy = 0x0082;
    private const int VkEscape = 0x1B;
    private const int VkF = 0x46;
    private const int VkT = 0x54;

    private static readonly nint HwndTopmost = new(-1);

    private static WndProc? _subclass;
    private static nint _oldWndProc;
    private static nint _hookedHwnd;
    private static Action? _onLeave;

    public static nint FindVoWindow(nint hostHwnd = 0)
    {
        nint found = 0;
        uint pid = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == hostHwnd)
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint windowPid);
            if (windowPid != pid || !IsWindowVisible(hwnd))
            {
                return true;
            }

            StringBuilder cls = new(256);
            if (GetClassName(hwnd, cls, cls.Capacity) <= 0)
            {
                return true;
            }

            string name = cls.ToString();
            if (name.Contains("WinUI", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Xaml", StringComparison.OrdinalIgnoreCase)
                || name.Contains("DesktopChildSiteBridge", StringComparison.OrdinalIgnoreCase)
                || name.Contains("IME", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (name.Contains("mpv", StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }

            return true;
        }, 0);
        return found;
    }

    public static bool TryGetMonitorRect(nint hwnd, out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        if (hwnd == 0)
        {
            return false;
        }

        nint monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        MonitorInfo info = new() { CbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        x = info.RcMonitor.Left;
        y = info.RcMonitor.Top;
        width = info.RcMonitor.Right - info.RcMonitor.Left;
        height = info.RcMonitor.Bottom - info.RcMonitor.Top;
        return width > 0 && height > 0;
    }

    public static string Geometry(int x, int y, int width, int height)
    {
        string sx = x >= 0 ? "+" + x.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : x.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string sy = y >= 0 ? "+" + y.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return width.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "x"
            + height.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + sx
            + sy;
    }

    public static bool CoversMonitor(nint hwnd, nint monitorHwnd, int slack = 16)
    {
        if (hwnd == 0 || !IsWindow(hwnd)
            || !TryGetMonitorRect(monitorHwnd != 0 ? monitorHwnd : hwnd, out int x, out int y, out int width, out int height)
            || !GetWindowRect(hwnd, out Rect rect))
        {
            return false;
        }

        return Math.Abs(rect.Left - x) <= slack
            && Math.Abs(rect.Top - y) <= slack
            && Math.Abs(rect.Right - rect.Left - width) <= slack
            && Math.Abs(rect.Bottom - rect.Top - height) <= slack;
    }

    public static void CoverMonitor(nint hwnd, nint monitorHwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            return;
        }

        if (!TryGetMonitorRect(monitorHwnd != 0 ? monitorHwnd : hwnd, out int x, out int y, out int width, out int height))
        {
            ShowWindow(hwnd, SwShow);
            return;
        }

        nint popup = (nint)(WsPopup | WsVisible | WsClipSiblings | WsClipChildren);
        _ = SetWindowLongPtr(hwnd, GwlStyle, popup);
        nint ex = GetWindowLongPtr(hwnd, GwlExStyle);
        _ = SetWindowLongPtr(hwnd, GwlExStyle, (nint)((long)ex | WsExTopmost | WsExAppWindow));
        SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SwpShowWindow | SwpFrameChanged | SwpNoCopyBits);
        ShowWindow(hwnd, SwShow);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
    }

    public static void HookLeaveKeys(nint hwnd, Action onLeave)
    {
        UnhookLeaveKeys();
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            return;
        }

        _onLeave = onLeave ?? throw new ArgumentNullException(nameof(onLeave));
        _subclass = OnSubclass;
        _hookedHwnd = hwnd;
        nint fn = Marshal.GetFunctionPointerForDelegate(_subclass);
        _oldWndProc = SetWindowLongPtr(hwnd, GwlpWndProc, fn);
        if (_oldWndProc == 0)
        {
            _subclass = null;
            _hookedHwnd = 0;
            _onLeave = null;
        }
    }

    /// <summary>True while a window procedure hook is installed on <paramref name="hwnd"/>.</summary>
    public static bool IsHooked(nint hwnd) => hwnd != 0 && _hookedHwnd == hwnd && _oldWndProc != 0;

    public static void UnhookLeaveKeys()
    {
        // Only restore if the hook is still the current procedure: mpv may have
        // destroyed the window (HWND values are recycled) or replaced the proc.
        if (_hookedHwnd != 0 && _oldWndProc != 0 && IsWindow(_hookedHwnd) && _subclass is not null
            && GetWindowLongPtr(_hookedHwnd, GwlpWndProc) == Marshal.GetFunctionPointerForDelegate(_subclass))
        {
            _ = SetWindowLongPtr(_hookedHwnd, GwlpWndProc, _oldWndProc);
        }

        _hookedHwnd = 0;
        _oldWndProc = 0;
        _subclass = null;
        _onLeave = null;
    }

    private static nint OnSubclass(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        // Reverse-P/Invoke frame on mpv's window thread: a managed exception here
        // is fatal for the process, so nothing below may throw.
        try
        {
            if (msg is WmKeyDown or WmSysKeyDown)
            {
                int vk = unchecked((int)wParam);
                if (vk is VkEscape or VkF or VkT)
                {
                    _onLeave?.Invoke();
                    return 1;
                }
            }

            if (msg == WmClose)
            {
                _onLeave?.Invoke();
                return 0;
            }

            if (msg == WmNcDestroy)
            {
                // The window is going away: forward once, then forget the hook so a
                // later unhook cannot write into a recycled HWND.
                nint old = _oldWndProc;
                _hookedHwnd = 0;
                _oldWndProc = 0;
                return old == 0 ? nint.Zero : CallWindowProc(old, hWnd, msg, wParam, lParam);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Swallow: see above.
        }

        return _oldWndProc == 0 ? nint.Zero : CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint CbSize;
        public Rect RcMonitor;
        public Rect RcWork;
        public uint DwFlags;
    }
}
