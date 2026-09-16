using Penrose.Core.Playback;
using Penrose.Core.Sources;
using Penrose.Sources.Emby;

namespace Penrose.Sources.Emby.Tests;

public sealed class EmbyPlaybackInfoParserTests
{
    private static readonly Uri Server = new("https://emby.example/");

    [Fact]
    public void Directstream_when_directplay_false()
    {
        PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
            """
            {
              "PlaySessionId": "sess-emby",
              "MediaSources": [
                {
                  "Id": "emby-src",
                  "Protocol": "Http",
                  "SupportsDirectPlay": false,
                  "SupportsDirectStream": true,
                  "DirectStreamUrl": "/emby/videos/99/stream.mkv?Static=true",
                  "RequiredHttpHeaders": { "X-Emby-Token": "secret-token" }
                }
              ]
            }
            """,
            Server,
            "99");
        Assert.Equal(PlayMethod.DirectStream, candidate.Method);
        Assert.Equal("emby", candidate.ProviderId);
        Assert.Equal("sess-emby", candidate.PlaySessionId);
        Assert.Equal("secret-token", candidate.Headers["X-Emby-Token"]);
        Assert.StartsWith("https://emby.example/emby/videos/99/stream.mkv", candidate.Uri.ToString());
    }

    [Fact]
    public void File_protocol_falls_back_to_direct_stream_when_path_is_missing()
    {
        string missing = Path.Combine(Path.GetTempPath(), "mp-missing-" + Guid.NewGuid().ToString("N") + ".mkv");
        PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
            $$"""
            {
              "PlaySessionId": "sess-file",
              "MediaSources": [
                {
                  "Id": "emby-src",
                  "Protocol": "File",
                  "Path": {{ToJson(missing)}},
                  "SupportsDirectPlay": true,
                  "SupportsDirectStream": true,
                  "DirectStreamUrl": "/emby/videos/99/stream.mkv?Static=true"
                }
              ]
            }
            """,
            Server,
            "99");
        Assert.Equal(PlayMethod.DirectPlay, candidate.Method);
        Assert.True(candidate.SupportsPathMapping);
        Assert.Equal(missing, candidate.ServerPath);
        Assert.StartsWith("https://emby.example/emby/videos/99/stream.mkv", candidate.Uri.ToString());
        Assert.False(candidate.Uri.IsFile);
    }

    [Fact]
    public void File_protocol_uses_local_path_when_it_exists()
    {
        string path = Path.Combine(Path.GetTempPath(), "mp-local-" + Guid.NewGuid().ToString("N") + ".mkv");
        File.WriteAllBytes(path, [0]);
        try
        {
            PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
                $$"""
                {
                  "PlaySessionId": "sess-local",
                  "MediaSources": [
                    {
                      "Id": "emby-src",
                      "Protocol": "File",
                      "Path": {{ToJson(path)}},
                      "SupportsDirectPlay": true,
                      "DirectStreamUrl": "/emby/videos/99/stream.mkv?Static=true"
                    }
                  ]
                }
                """,
                Server,
                "99",
                allowServerFilePaths: true);
            Assert.Equal(PlayMethod.DirectPlay, candidate.Method);
            Assert.True(candidate.Uri.IsFile);
            Assert.Equal(Path.GetFullPath(path), candidate.Uri.LocalPath, ignoreCase: true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void File_protocol_is_not_probed_unless_opted_in()
    {
        string path = Path.Combine(Path.GetTempPath(), "mp-local-" + Guid.NewGuid().ToString("N") + ".mkv");
        File.WriteAllBytes(path, [0]);
        try
        {
            PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
                $$"""
                {
                  "PlaySessionId": "sess-local",
                  "MediaSources": [
                    {
                      "Id": "emby-src",
                      "Protocol": "File",
                      "Path": {{ToJson(path)}},
                      "SupportsDirectPlay": true,
                      "DirectStreamUrl": "/emby/videos/99/stream.mkv?Static=true"
                    }
                  ]
                }
                """,
                Server,
                "99");
            // Default: the server's filesystem path (possibly a UNC to an attacker
            // host) is never touched; the server's own stream route is used.
            Assert.False(candidate.Uri.IsFile);
            Assert.Equal(new Uri(Server, "/emby/videos/99/stream.mkv?Static=true"), candidate.Uri);
            Assert.True(candidate.SupportsPathMapping);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Linux_filesystem_path_is_not_treated_as_server_route()
    {
        PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
            """
            {
              "PlaySessionId": "sess-linux",
              "MediaSources": [
                {
                  "Id": "emby-src",
                  "Protocol": "File",
                  "Path": "/mnt/media/movie.mkv",
                  "SupportsDirectPlay": true,
                  "SupportsDirectStream": true,
                  "TranscodingUrl": "/emby/videos/99/master.m3u8"
                }
              ]
            }
            """,
            Server,
            "99");
        Assert.Equal(PlayMethod.Transcode, candidate.Method);
        Assert.Equal(new Uri(Server, "/emby/videos/99/master.m3u8"), candidate.Uri);
    }

    [Fact]
    public void Subtitle_without_delivery_url_is_skipped()
    {
        PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
            """
            {
              "PlaySessionId": "sess-sub",
              "MediaSources": [
                {
                  "Id": "emby-src",
                  "Protocol": "Http",
                  "SupportsDirectPlay": true,
                  "DirectStreamUrl": "/emby/videos/99/stream.mkv",
                  "MediaStreams": [
                    { "Type": "Subtitle", "IsExternal": true, "Codec": "srt", "Path": "D:\\media\\movie.srt" },
                    { "Type": "Subtitle", "IsExternal": true, "Codec": "srt", "Path": "\\\\nas\\share\\movie.srt" },
                    { "Type": "Subtitle", "IsExternal": true, "Codec": "ass", "DeliveryMethod": "Embed", "DeliveryUrl": "/x.ass" },
                    { "Type": "Subtitle", "IsExternal": true, "Codec": "ass", "DeliveryMethod": "External", "DeliveryUrl": "/emby/Videos/99/Subtitles/3/Stream.ass" }
                  ]
                }
              ]
            }
            """,
            Server,
            "99");
        ExternalSubtitle only = Assert.Single(candidate.ExternalSubtitles);
        Assert.Equal(new Uri(Server, "/emby/Videos/99/Subtitles/3/Stream.ass"), only.Uri);
    }

    [Fact]
    public void External_subs_prefer_delivery_url_over_server_path()
    {
        PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
            """
            {
              "PlaySessionId": "sess-sub",
              "MediaSources": [
                {
                  "Id": "emby-src",
                  "Protocol": "Http",
                  "SupportsDirectPlay": true,
                  "DirectStreamUrl": "/emby/videos/99/stream.mkv",
                  "MediaStreams": [
                    {
                      "Type": "Subtitle",
                      "IsExternal": true,
                      "Language": "chi",
                      "Path": "D:\\server\\film.chi.ass",
                      "DeliveryUrl": "/emby/Videos/99/Subtitles/2/Stream.ass"
                    }
                  ]
                }
              ]
            }
            """,
            Server,
            "99");
        Assert.Equal(
            new Uri("https://emby.example/emby/Videos/99/Subtitles/2/Stream.ass"),
            candidate.ExternalSubtitles[0].Uri);
    }

    [Fact]
    public void Missing_sources_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EmbyPlaybackInfoParser.Parse("""{ "PlaySessionId": "x", "MediaSources": [] }""", Server, "1"));
    }

    [Fact]
    public void Zero_byte_version_is_skipped_for_the_next_one()
    {
        PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
            """
            {
              "PlaySessionId": "s",
              "MediaSources": [
                { "Id": "gone", "Protocol": "Http", "SupportsDirectPlay": true, "SupportsDirectStream": true, "DirectStreamUrl": "/videos/1/gone.mp4", "Size": 0 },
                { "Id": "ok", "Protocol": "Http", "SupportsDirectPlay": true, "SupportsDirectStream": true, "DirectStreamUrl": "/videos/1/ok.mkv", "Size": 45450849 }
              ]
            }
            """,
            Server,
            "1");
        Assert.Equal("ok", candidate.MediaSourceId);
        Assert.Contains("/ok.mkv", candidate.Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Preferred_version_wins_when_it_still_has_bytes()
    {
        const string json = """
            {
              "PlaySessionId": "s",
              "MediaSources": [
                { "Id": "hd", "Protocol": "Http", "SupportsDirectPlay": true, "SupportsDirectStream": true, "DirectStreamUrl": "/videos/1/hd.mkv", "Size": 100 },
                { "Id": "uhd", "Protocol": "Http", "SupportsDirectPlay": true, "SupportsDirectStream": true, "DirectStreamUrl": "/videos/1/uhd.mkv", "Size": 200 },
                { "Id": "gone", "Protocol": "Http", "SupportsDirectPlay": true, "SupportsDirectStream": true, "DirectStreamUrl": "/videos/1/gone.mkv", "Size": 0 }
              ]
            }
            """;
        Assert.Equal("uhd", EmbyPlaybackInfoParser.Parse(json, Server, "1", false, "uhd").MediaSourceId);
        // A missing file or an unknown id falls back to the first good version.
        Assert.Equal("hd", EmbyPlaybackInfoParser.Parse(json, Server, "1", false, "gone").MediaSourceId);
        Assert.Equal("hd", EmbyPlaybackInfoParser.Parse(json, Server, "1", false, "nope").MediaSourceId);
        Assert.Equal("hd", EmbyPlaybackInfoParser.Parse(json, Server, "1", false, null).MediaSourceId);
    }

    [Fact]
    public void Strm_openlist_path_is_preferred_over_direct_stream()
    {
        PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
            """
            {
              "PlaySessionId": "sess-strm",
              "MediaSources": [
                {
                  "Id": "strm-src",
                  "Protocol": "Http",
                  "Path": "http://192.168.1.88:5244/d/movie.mkv?sign=placeholder",
                  "SupportsDirectPlay": true,
                  "SupportsDirectStream": true,
                  "DirectStreamUrl": "/emby/videos/99/stream.mkv?Static=true",
                  "RequiredHttpHeaders": { "X-Emby-Token": "secret-token" }
                }
              ]
            }
            """,
            new Uri("http://192.168.1.88:8096/"),
            "99");
        Assert.Equal(PlayMethod.DirectPlay, candidate.Method);
        Assert.Equal(MediaSourceKind.StrmRelay, candidate.SourceKind);
        Assert.Equal("192.168.1.88", candidate.Uri.Host);
        Assert.Equal(5244, candidate.Uri.Port);
        Assert.Equal("/d/movie.mkv", candidate.Uri.AbsolutePath);
        Assert.Equal("secret-token", candidate.Headers["X-Emby-Token"]);
    }

    [Fact]
    public void Loopback_strm_path_falls_back_to_direct_stream()
    {
        PlaybackCandidate candidate = EmbyPlaybackInfoParser.Parse(
            """
            {
              "PlaySessionId": "sess-loop",
              "MediaSources": [
                {
                  "Id": "strm-src",
                  "Protocol": "Http",
                  "Path": "http://127.0.0.1:5244/d/movie.mkv",
                  "SupportsDirectPlay": true,
                  "SupportsDirectStream": true,
                  "DirectStreamUrl": "/emby/videos/99/stream.mkv?Static=true"
                }
              ]
            }
            """,
            Server,
            "99");
        Assert.Equal(MediaSourceKind.ServerDirectPlay, candidate.SourceKind);
        Assert.Equal(new Uri("https://emby.example/emby/videos/99/stream.mkv?Static=true"), candidate.Uri);
    }

    [Fact]
    public async Task Refresh_without_item_id_throws()
    {
        using EmbyClient client = new(Server);
        EmbyPlaybackResolver resolver = new(client, "user-1");
        PlaybackCandidate expired = new()
        {
            Method = PlayMethod.DirectPlay,
            Uri = new Uri("https://emby.example/smartstrm"),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.RefreshAsync(expired, DeviceProfileBuilder.ReferenceHost()));
    }

    [Fact]
    public void W1_emby_profile_json_is_independent_payload()
    {
        string json = EmbyDeviceProfileJson.Serialize(DeviceProfileBuilder.Build(DeviceProfileBuilder.ReferenceHost()));
        Assert.Contains("Penrose", json, StringComparison.Ordinal);
        Assert.Contains("truehd", json, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxStaticBitrate", json, StringComparison.Ordinal);
    }

    private static string ToJson(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
