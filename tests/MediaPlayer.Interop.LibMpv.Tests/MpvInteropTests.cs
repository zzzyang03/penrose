using MediaPlayer.Interop.LibMpv;

namespace MediaPlayer.Interop.LibMpv.Tests;

public sealed class MpvInteropTests
{
    [Fact]
    public void Utf8z_is_null_terminated()
    {
        byte[] bytes = MpvNode.Utf8Z("vo");
        Assert.Equal(0, bytes[^1]);
        Assert.Equal((byte)'v', bytes[0]);
    }

    [Fact]
    public void ResolvePath_does_not_throw_when_binary_missing()
    {
        string? path = MpvNativeLibrary.ResolvePath();
        Assert.True(path is null || File.Exists(path));
    }

    [Fact]
    public void TryLoad_fails_cleanly_without_dll_on_this_host()
    {
        if (MpvNativeLibrary.ResolvePath() is not null)
        {
            return;
        }

        bool loaded = MpvNativeLibrary.TryLoad(out string? path, out string? error);
        Assert.False(loaded);
        Assert.Null(path);
        Assert.Contains("libmpv", error, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsFact]
    public void Locked_dll_loads_on_windows()
    {
        Assert.True(MpvNativeLibrary.TryLoad(out string? path, out string? error), error);
        Assert.True(File.Exists(path));
        uint packed = MpvNativeLibrary.ClientApiVersion();
        Assert.True(packed > 0, MpvNativeLibrary.FormatClientApiVersion(packed));
    }

    [Fact]
    public void Node_free_contents_on_zeroed_struct_is_safe_without_native()
    {
        MpvNodeNative node = default;
        Assert.Equal(0, node.Format);
        Assert.Equal(0, node.Union);
    }
}
