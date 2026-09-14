using System.Runtime.InteropServices;

namespace MediaPlayer.Interop.LibMpv;

/// <summary>
/// Owns an mpv_handle. ReleaseHandle is the only path that calls
/// mpv_terminate_destroy.
/// </summary>
public sealed class MpvHandle : SafeHandle
{
    public MpvHandle()
        : base(invalidHandleValue: 0, ownsHandle: true)
    {
    }

    public MpvHandle(nint native)
        : this()
    {
        SetHandle(native);
    }

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle()
    {
        nint ctx = handle;
        if (ctx != 0)
        {
            NativeMethods.mpv_terminate_destroy(ctx);
        }

        return true;
    }
}
