using Penrose.Core.Playback;
using Penrose.Core.Settings;

namespace Penrose.Core.Tests;

public sealed class TrackListParserTests
{
    [Fact]
    public void Parses_mpv_track_list_json()
    {
        const string json =
            """
            [
              {"id":1,"type":"video","title":"","lang":"und","selected":true,"external":false},
              {"id":2,"type":"audio","title":"Commentary","lang":"eng","selected":true,"external":false},
              {"id":1,"type":"sub","title":"CHS","lang":"chi","selected":false,"external":true}
            ]
            """;

        IReadOnlyList<TrackInfo> tracks = TrackListParser.Parse(json);
        Assert.Equal(3, tracks.Count);
        Assert.Equal("audio", tracks[1].Type);
        Assert.Equal("eng", tracks[1].Language);
        Assert.Equal("Commentary", tracks[1].Title);
        Assert.True(tracks[1].Selected);
        Assert.True(tracks[2].External);
        Assert.False(tracks[2].Selected);
        Assert.Single(TrackListParser.OfType(tracks, "sub"));
    }

    [Fact]
    public void Empty_or_invalid_json_is_empty_list()
    {
        Assert.Empty(TrackListParser.Parse(null));
        Assert.Empty(TrackListParser.Parse(""));
        Assert.Empty(TrackListParser.Parse("(unavailable)"));
        Assert.Empty(TrackListParser.Parse("{not-an-array}"));
    }

    [Fact]
    public void Spdif_format_is_passthrough()
    {
        Assert.True(AudioPassthrough.IsSpdifFormat("spdif-ac3"));
        Assert.True(AudioPassthrough.IsSpdifFormat("spdif-truehd"));
        Assert.True(AudioPassthrough.IsSpdifFormat("iec61937"));
        Assert.False(AudioPassthrough.IsSpdifFormat("float"));
        Assert.False(AudioPassthrough.IsSpdifFormat("s32"));
        Assert.False(AudioPassthrough.IsSpdifFormat(null));
    }

    [Fact]
    public void Simple_settings_roundtrip_enums_as_names()
    {
        SimpleSettings settings = new()
        {
            AudioPolicy = AudioPolicy.Bitstream,
            Volume = 42,
            NightMode = true,
            Mute = true,
            SubCodepage = "gbk",
            SubAssOverride = "scale",
            Language = "en",
            Servers =
            [
                new LibraryServerSettings
                {
                    Kind = "emby",
                    BaseUrl = "http://emby.lan:8097/",
                    UserName = "user",
                    UserId = "user-1",
                },
            ],
        };
        string json = SimpleSettingsSerializer.ToJson(settings);
        Assert.Contains("Bitstream", json, StringComparison.Ordinal);
        SimpleSettings loaded = SimpleSettingsSerializer.FromJson(json);
        Assert.Equal(AudioPolicy.Bitstream, loaded.AudioPolicy);
        Assert.Equal(42, loaded.Volume);
        Assert.True(loaded.NightMode);
        Assert.True(loaded.Mute);
        Assert.Equal("gbk", loaded.SubCodepage);
        Assert.Equal("scale", loaded.SubAssOverride);
        Assert.Equal("en", loaded.Language);
        Assert.Equal("emby", loaded.Servers[0].Kind);
        Assert.Equal("user", loaded.Servers[0].UserName);
        Assert.Equal("zh-CN", SimpleSettingsSerializer.FromJson(null).Language);
        Assert.Equal(AudioPolicy.SystemCompatible, SimpleSettingsSerializer.FromJson("{not json").AudioPolicy);
    }

    [Fact]
    public void Quality_preset_high_enables_deband()
    {
        IReadOnlyDictionary<string, string> high = QualityPresetOptions.ToProperties(QualityPreset.High);
        Assert.Equal("yes", high["deband"]);
        Assert.Equal("ewa_lanczos", high["scale"]);
        IReadOnlyDictionary<string, string> fast = QualityPresetOptions.ToProperties(QualityPreset.Fast);
        Assert.Equal("bilinear", fast["scale"]);
        Assert.Equal("no", fast["deband"]);
    }
}
