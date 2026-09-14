namespace Penrose.VideoSurface;

/// <summary>
/// Skip composition rebind / display-target refresh while the user is dragging
/// or interactively resizing the window (WM_ENTERSIZEMOVE … WM_EXITSIZEMOVE).
/// Forced binds (attach, VO rebuild) still go through.
/// </summary>
public static class SurfaceBindGate
{
    public static bool ShouldSkipBind(bool interactiveMove, bool force) =>
        interactiveMove && !force;

    public static bool ShouldSkipDisplayRefresh(bool interactiveMove) =>
        interactiveMove;
}
