using System.Runtime.InteropServices;

namespace MediaPlayer.Interop.LibMpv;

internal static partial class NativeMethods
{
    public const string LibraryName = "mpv-2";

    static NativeMethods()
    {
        MpvNativeLibrary.RegisterDllImportResolver();
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint mpv_create();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(nint ctx);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(nint ctx);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command(nint ctx, nint args);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command_async(nint ctx, ulong replyUserdata, nint args);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(nint ctx, byte[] name, byte[] data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property_string(nint ctx, byte[] name, byte[] data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint mpv_get_property_string(nint ctx, byte[] name);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_get_property(nint ctx, byte[] name, int format, nint data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_observe_property(nint ctx, ulong userdata, byte[] name, int format);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint mpv_wait_event(nint ctx, double timeout);

    /// <summary>Enables <c>MPV_EVENT_LOG_MESSAGE</c> at <paramref name="minLevel"/> ("warn", "info", "v", "debug", "no").</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_request_log_messages(nint ctx, byte[] minLevel);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_wakeup(nint ctx);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_free(nint data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_free_node_contents(nint node);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint mpv_error_string(int error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint mpv_client_api_version();
}
