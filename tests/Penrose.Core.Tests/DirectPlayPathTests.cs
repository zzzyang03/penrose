using Penrose.Core.Sources;

namespace Penrose.Core.Tests;

public sealed class DirectPlayPathTests
{
    private static readonly Uri Emby = new("http://192.168.1.88:8096/");

    [Fact]
    public void OpenList_on_another_port_is_an_external_strm_url()
    {
        const string path = "http://192.168.1.88:5244/d/movie.mkv?sign=placeholder";
        Assert.True(DirectPlayPath.IsExternalHttpPath(path, Emby));
        Assert.True(DirectPlayPath.TryChooseRemotePlay(
            path, "/emby/videos/1/stream.mkv?Static=true", Emby, out string url, out MediaSourceKind kind));
        Assert.Equal(path, url);
        Assert.Equal(MediaSourceKind.StrmRelay, kind);
    }

    [Fact]
    public void Loopback_strm_falls_back_to_the_server_stream()
    {
        Assert.False(DirectPlayPath.IsExternalHttpPath("http://127.0.0.1:5244/d/movie.mkv", Emby));
        Assert.True(DirectPlayPath.TryChooseRemotePlay(
            "http://127.0.0.1:5244/d/movie.mkv",
            "/emby/videos/1/stream.mkv",
            Emby,
            out string url,
            out MediaSourceKind kind));
        Assert.Equal("/emby/videos/1/stream.mkv", url);
        Assert.Equal(MediaSourceKind.ServerDirectPlay, kind);
    }

    [Fact]
    public void Same_origin_download_path_keeps_direct_stream()
    {
        Assert.False(DirectPlayPath.IsExternalHttpPath("http://192.168.1.88:8096/Items/1/Download", Emby));
        Assert.True(DirectPlayPath.TryChooseRemotePlay(
            "http://192.168.1.88:8096/Items/1/Download",
            "/emby/videos/1/stream.mkv",
            Emby,
            out string url,
            out MediaSourceKind kind));
        Assert.Equal("/emby/videos/1/stream.mkv", url);
        Assert.Equal(MediaSourceKind.ServerDirectPlay, kind);
    }

    [Fact]
    public void Http_path_is_used_when_there_is_no_direct_stream()
    {
        Assert.True(DirectPlayPath.TryChooseRemotePlay(
            "https://cdn.example/a.mkv",
            directStreamUrl: null,
            new Uri("https://emby.example/"),
            out string url,
            out MediaSourceKind kind));
        Assert.Equal("https://cdn.example/a.mkv", url);
        Assert.Equal(MediaSourceKind.StrmRelay, kind);
    }
}
