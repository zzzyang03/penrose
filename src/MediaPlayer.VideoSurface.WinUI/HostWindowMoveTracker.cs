using System.Runtime.InteropServices;

namespace MediaPlayer.VideoSurface.WinUI;

/// <summary>
/// WM_ENTERSIZEMOVE / WM_EXITSIZEMOVE on the WinUI HWND via comctl subclass.
/// </summary>
internal sealed class HostWindowMoveTracker : IDisposable
{
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const uint WmNcDestroy = 0x0082;
    private const nuint SubclassId = 0x4D50564D;

    private readonly nint _hwnd;
    private readonly SubclassProc _proc;
    private bool _hooked;

    public HostWindowMoveTracker(nint hwnd)
    {
        _hwnd = hwnd;
        _proc = OnSubclass;
        _hooked = hwnd != 0 && SetWindowSubclass(hwnd, _proc, SubclassId, 0);
    }

    public bool IsHooked => _hooked;

    public event EventHandler? EnterSizeMove;

    public event EventHandler? ExitSizeMove;

    public void Dispose()
    {
        if (!_hooked)
        {
            return;
        }

        _ = RemoveWindowSubclass(_hwnd, _proc, SubclassId);
        _hooked = false;
    }

    private nint OnSubclass(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        // Reverse-P/Invoke frame: a managed exception escaping here is fatal.
        try
        {
            switch (msg)
            {
                case WmEnterSizeMove:
                    EnterSizeMove?.Invoke(this, EventArgs.Empty);
                    break;
                case WmExitSizeMove:
                    ExitSizeMove?.Invoke(this, EventArgs.Empty);
                    break;
                case WmNcDestroy:
                    _ = RemoveWindowSubclass(hWnd, _proc, SubclassId);
                    _hooked = false;
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Subscribers log their own failures; the window procedure must return.
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);
}
