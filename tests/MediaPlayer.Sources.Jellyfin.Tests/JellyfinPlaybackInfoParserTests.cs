using MediaPlayer.Core.Playback;
using MediaPlayer.Core.Sources;
using MediaPlayer.Sources.Jellyfin;

namespace MediaPlayer.Sources.Jellyfin.Tests;

public sealed class JellyfinPlaybackInfoParserTests
{
    private static readonly Uri Server = new("https://jf.example/");

    [Fact]
    public void Directplay_http_copies_headers_session_and_external_sub()
    {
        PlaybackCandidate candidate = JellyfinPlaybackInfoParser.Parse(
            """
            {
              "PlaySessionId": "sess-jf",
              "MediaSources": [
                {
                  "Id": "src-1",
                  "Protocol": "Http",
                  "Path": "https://jf.example/Items/abc/Download",
                  "SupportsDirectPlay": true,
                  "SupportsDirectStream": true,
                  "DirectStreamUrl": "/videos/abc/stream.mkv",
                  "RequiredHttpHeaders": { "Authorization": "MediaBrowser Token=secret-token" },
                  "MediaStreams": [
                    {
                      "Type": "Subtitle",
                      "IsExternal": true,
                      "Codec": "ass",
                      "Language": "chi",
                      "Path": "/srv/media/movie.chi.ass",
                      "DeliveryMethod": "External",
                      "DeliveryUrl": "/Items/abc/Subtitles/2/Stream.ass"
                    },
                    {
                      "Type": "Subtitle",
                      "IsExternal": true,
                      "Codec": "srt",
                      "Language": "eng",
                      "Path": "/srv/media/movie.eng.srt"
                    }
                  ]
                }
              ]
            }
            """,
            Server,
            "abc");

        Assert.Equal(PlayMethod.DirectPlay, candidate.Method);
        Assert.Equal("sess-jf", candidate.PlaySessionId);
        Assert.Equal("jellyfin", candidate.ProviderId);
        Assert.Equal("MediaBrowser Token=secret-token", candidate.Headers["Authorization"]);
        ExternalSubtitle sub = Assert.Single(candidate.ExternalSubtitles);
        Assert.Equal(new Uri("https://jf.example/Items/abc/Subtitles/2/Stream.ass"), sub.Uri);
        Assert.Equal("chi", sub.Language);
    }

    [Fact]
    public void File_protocol_falls_back_to_direct_stream_when_path_is_missing()
    {
        string missing = Path.Combine(Path.GetTempPath(), "mp-missing-" + Guid.NewGuid().ToString("N") + ".mkv");
        PlaybackCandidate candidate = JellyfinPlaybackInfoParser.Parse(
            $$"""
            {
              "PlaySessionId": "sess-file",
              "MediaSources": [
                {
                  "Id": "src-file",
                  "Protocol": "File",
                  "Path": {{ToJson(missing)}},
                  "SupportsDirectPlay": true,
                  "SupportsDirectStream": true,
                  "DirectStreamUrl": "/videos/abc/stream.mkv"
                }
              ]
            }
            """,
            Server,
            "file-1");
        Assert.Equal(PlayMethod.DirectPlay, candidate.Method);
        Assert.True(candidate.SupportsPathMapping);
        Assert.Equal(missing, candidate.ServerPath);
        Assert.StartsWith("https://jf.example/videos/abc/stream.mkv", candidate.Uri.ToString());
        Assert.False(candidate.Uri.IsFile);
    }

    [Fact]
    public void Transcode_when_direct_flags_off()
    {
        PlaybackCandidate candidate = JellyfinPlaybackInfoParser.Parse(
            """
            {
              "PlaySessionId": "sess-t",
              "MediaSources": [
                {
                  "Id": "src-t",
                  "Protocol": "Http",
                  "SupportsDirectPlay": false,
                  "SupportsDirectStream": false,
                  "TranscodingUrl": "/videos/abc/master.m3u8?api_key=secret-token"
                }
              ]
            }
            """,
            Server,
            "abc");
        Assert.Equal(PlayMethod.Transcode, candidate.Method);
        Assert.Contains("master.m3u8", candidate.Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void W1_device_profile_json_omits_dts_from_nothing_but_lists_dolby()
    {
        string json = JellyfinDeviceProfileJson.Serialize(DeviceProfileBuilder.Build(DeviceProfileBuilder.ReferenceHost()));
        Assert.Contains("truehd", json, StringComparison.Ordinal);
        Assert.Contains("eac3", json, StringComparison.Ordinal);
        Assert.Contains("dts", json, StringComparison.Ordinal);
        Assert.Contains("ass", json, StringComparison.Ordinal);
        Assert.Contains("MediaPlayer", json, StringComparison.Ordinal);
    }

    private static string ToJson(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
