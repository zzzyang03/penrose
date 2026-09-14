using System.Runtime.InteropServices;
using System.Text;

namespace MediaPlayer.Interop.LibMpv;

[StructLayout(LayoutKind.Sequential)]
public struct MpvNodeNative
{
    public nint Union;
    public int Format;
}

/// <summary>
/// Helpers for mpv_node lifetime. Call <see cref="FreeContents"/> exactly once
/// per node filled by libmpv. Nested values are freed by libmpv.
/// </summary>
public static class MpvNode
{
    public static byte[] Utf8Z(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int byteCount = Encoding.UTF8.GetByteCount(value);
        byte[] buffer = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
        return buffer;
    }

    public static string? PtrToStringUtf8(nint ptr) =>
        ptr == 0 ? null : Marshal.PtrToStringUTF8(ptr);

    public static void Free(nint ptr)
    {
        if (ptr != 0)
        {
            NativeMethods.mpv_free(ptr);
        }
    }

    public static void FreeContents(ref MpvNodeNative node)
    {
        unsafe
        {
            fixed (MpvNodeNative* p = &node)
            {
                NativeMethods.mpv_free_node_contents((nint)p);
            }
        }

        node = default;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventNative
{
    public int EventId;
    public int Error;
    public ulong ReplyUserdata;
    public nint Data;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventEndFileNative
{
    public int Reason;
    public int Error;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventPropertyNative
{
    public nint Name;
    public int Format;
    public nint Data;
}

/// <summary><c>mpv_event_log_message</c>: module prefix, level name, text (with trailing newline), numeric level.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MpvEventLogMessageNative
{
    public nint Prefix;
    public nint Level;
    public nint Text;
    public int LogLevel;
}
