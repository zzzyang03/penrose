using Penrose.Core.Playback;
using Penrose.Core.Ui;

namespace Penrose.Core.Tests;

public sealed class FormatBadgesTests
{
    private static TrackInfo Audio(string codec, string? profile = null, int channels = 6, string layout = "5.1(side)") =>
        new(2, "audio", "eng", null, true, false, Codec: codec, CodecProfile: profile, Channels: channels, ChannelLayout: layout);

    private static TrackInfo Video(int? dv = null, int w = 3840, int h = 2160) =>
        new(1, "video", null, null, true, false, Codec: "hevc", CodecProfile: "Main 10", DolbyVisionProfile: dv, Width: w, Height: h);

    [Fact]
    public void Dolby_vision_and_atmos_from_profile()
    {
        MediaFormatInfo info = new(
            "pq", "bt.2020", 3840, 2160,
            Video(dv: 8),
            Audio("eac3", "Dolby Digital Plus + Dolby Atmos", 8, "7.1"));

        Assert.Equal([FormatBadges.DolbyVision, FormatBadges.DolbyAtmos], FormatBadges.For(info));
        Assert.Equal(DynamicRange.DolbyVision, FormatBadges.RangeOf(info));
    }

    [Fact]
    public void Dolby_vision_lights_hdr_even_without_pq_gamma()
    {
        // Profile 5 (IPTPQc2) is not reported as "pq" by every build; the RPU still means HDR.
        MediaFormatInfo info = new("auto", "bt.2020", 3840, 2160, Video(dv: 5), null);
        Assert.Equal(DynamicRange.DolbyVision, FormatBadges.RangeOf(info));
        Assert.Equal([FormatBadges.DolbyVision], FormatBadges.For(info));
    }

    [Fact]
    public void Hdr10plus_metadata_gets_a_chip_and_lights_hdr()
    {
        MediaFormatInfo info = new("pq", "bt.2020", 3840, 2160, Video(), Audio("eac3"), Hdr10Plus: true);
        Assert.Equal([FormatBadges.Hdr10Plus], FormatBadges.For(info));
        Assert.Equal(DynamicRange.Hdr10Plus, FormatBadges.RangeOf(info));
        Assert.Equal("HDR10+", FormatBadges.Describe(DynamicRange.Hdr10Plus));
    }

    [Fact]
    public void Plain_hdr10_and_hlg_light_the_indicator_but_get_no_chip()
    {
        MediaFormatInfo hdr10 = new("pq", "bt.2020", 3840, 2160, Video(), Audio("truehd", "Dolby TrueHD", 8, "7.1"));
        Assert.Empty(FormatBadges.For(hdr10));
        Assert.Equal(DynamicRange.Hdr10, FormatBadges.RangeOf(hdr10));

        MediaFormatInfo hlg = new("hlg", "bt.2020", 1920, 1080, Video(w: 1920, h: 1080), Audio("dts", "DTS-HD MA"));
        Assert.Empty(FormatBadges.For(hlg));
        Assert.Equal(DynamicRange.Hlg, FormatBadges.RangeOf(hlg));
        Assert.Equal("HLG", FormatBadges.Describe(DynamicRange.Hlg));
    }

    [Fact]
    public void Ordinary_formats_resolution_and_channels_are_not_chips()
    {
        // 4K, 7.1, TrueHD, DD+, DTS-HD: all shown elsewhere (or not at all), never as a chip.
        Assert.Empty(FormatBadges.For(new MediaFormatInfo("bt.1886", "bt.709", 3840, 2160, Video(), Audio("truehd", null, 8, "7.1"))));
        Assert.Empty(FormatBadges.For(new MediaFormatInfo("bt.1886", "bt.709", 7680, 4320, Video(w: 7680, h: 4320), Audio("eac3"))));
        Assert.Empty(FormatBadges.For(new MediaFormatInfo("bt.1886", "bt.709", 1920, 1080, Video(w: 1920, h: 1080), Audio("dts", "DTS-HD MA"))));
        Assert.Equal(DynamicRange.Sdr, FormatBadges.RangeOf(new MediaFormatInfo("bt.1886", "bt.709", 3840, 2160, Video(), null)));
        Assert.Equal("SDR", FormatBadges.Describe(DynamicRange.Sdr));
    }

    [Fact]
    public void Dtsx_from_profile_and_audio_only_file()
    {
        Assert.Equal(
            [FormatBadges.DtsX],
            FormatBadges.For(new MediaFormatInfo("bt.1886", "bt.709", 1920, 1080, Video(w: 1920, h: 1080), Audio("dts", "DTS-HD MA + DTS:X", 8, "7.1"))));
        Assert.Equal(
            [FormatBadges.DolbyAtmos],
            FormatBadges.For(new MediaFormatInfo(null, null, null, null, null, Audio("truehd", "Dolby TrueHD + Dolby Atmos", 8, "7.1"))));
        Assert.Equal(DynamicRange.Sdr, FormatBadges.RangeOf(new MediaFormatInfo(null, null, null, null, null, Audio("aac"))));
    }

    [Fact]
    public void Every_chip_name_is_listed()
    {
        Assert.Equal([FormatBadges.DolbyVision, FormatBadges.Hdr10Plus, FormatBadges.DolbyAtmos, FormatBadges.DtsX], FormatBadges.All);
    }

    [Theory]
    [InlineData("5.1(side)", 6, "5.1")]
    [InlineData("7.1(wide)", 8, "7.1")]
    [InlineData("stereo", 2, "2.0")]
    [InlineData("mono", 1, "1.0")]
    [InlineData("2.1", 3, "2.1")]
    [InlineData(null, 6, "5.1")]
    [InlineData(null, 8, "7.1")]
    [InlineData(null, 5, "5ch")]
    [InlineData(null, null, null)]
    public void Channel_labels(string? layout, int? channels, string? expected)
    {
        Assert.Equal(expected, ChannelLayouts.Label(layout, channels));
    }

    [Fact]
    public void Channel_description_shows_downmix_arrow_only_when_layouts_differ()
    {
        Assert.Equal("7.1 \u2192 5.1", ChannelLayouts.Describe("7.1", 8, "5.1(side)", 6));
        Assert.Equal("5.1", ChannelLayouts.Describe("5.1(side)", 6, "5.1", 6));
        Assert.Equal("2.0", ChannelLayouts.Describe(null, null, "stereo", 2));
        Assert.True(ChannelLayouts.IsValidOverride(""));
        Assert.True(ChannelLayouts.IsValidOverride("auto"));
        Assert.True(ChannelLayouts.IsValidOverride("5.1"));
        Assert.False(ChannelLayouts.IsValidOverride("9.1"));
    }

    [Fact]
    public void Track_list_parser_reads_codec_channels_and_dolby_vision()
    {
        IReadOnlyList<TrackInfo> tracks = TrackListParser.Parse(
            """
            [{"id":1,"type":"video","selected":true,"codec":"hevc","codec-profile":"Main 10","demux-w":3840,"demux-h":2160,"dolby-vision-profile":8,"dolby-vision-level":6},
             {"id":1,"type":"audio","selected":true,"codec":"truehd","codec-profile":"Dolby TrueHD + Dolby Atmos","demux-channel-count":8,"demux-channels":"7.1","audio-channels":8}]
            """);

        TrackInfo video = tracks[0];
        Assert.Equal("hevc", video.Codec);
        Assert.Equal(8, video.DolbyVisionProfile);
        Assert.Equal(3840, video.Width);
        TrackInfo audio = tracks[1];
        Assert.Equal("truehd", audio.Codec);
        Assert.Equal("Dolby TrueHD + Dolby Atmos", audio.CodecProfile);
        Assert.Equal(8, audio.Channels);
        Assert.Equal("7.1", audio.ChannelLayout);
    }

    [Theory]
    [InlineData("eac3", "Dolby Digital Plus + Dolby Atmos", "Dolby Atmos")]
    [InlineData("eac3", "E-AC3", "E-AC3 (Dolby Digital Plus)")]
    [InlineData("eac3", "E-AC3+ATMOS", "Dolby Atmos")]
    [InlineData("ac3", "AC-3", "Dolby Digital")]
    [InlineData("ac3", "Dolby Digital", "Dolby Digital")]
    [InlineData("truehd", "Dolby TrueHD + Dolby Atmos", "Dolby Atmos")]
    [InlineData("truehd", "Dolby TrueHD", "Dolby TrueHD")]
    [InlineData("truehd", "TrueHD", "Dolby TrueHD")]
    [InlineData("dts", "DTS-HD MA", "DTS-HD MA")]
    [InlineData("dts", "DTS-HD Master Audio", "DTS-HD MA")]
    [InlineData("dts", "DTS-HD", "DTS-HD")]
    [InlineData("dts", "DTS-HD MA + DTS:X", "DTS:X")]
    [InlineData("dts", "DTS:X", "DTS:X")]
    [InlineData("dts", "DTS:X MA", "DTS:X")]
    [InlineData("dts", "DTS", "DTS")]
    [InlineData("aac", null, "AAC")]
    [InlineData("aac", "LC", "AAC")]
    [InlineData("aac", "HE-AAC", "AAC")]
    [InlineData("opus", null, "Opus")]
    [InlineData("flac", null, "FLAC")]
    [InlineData("mp3", null, "MP3")]
    [InlineData("vorbis", null, "Vorbis")]
    [InlineData("pcm_s16le", null, "PCM")]
    [InlineData("pcm_s24le", null, "PCM")]
    [InlineData("pcm_f32le", null, "PCM")]
    [InlineData("hevc", "Main 10", "hevc")]
    [InlineData("av1", null, "av1")]
    [InlineData("h264", "High", "h264")]
    [InlineData("", null, "")]
    [InlineData("ac3", "Dolby Digital Plus", "Dolby Digital Plus")]
    [InlineData(null, "Dolby Digital Plus", "Dolby Digital Plus")]
    [InlineData("unknowncodec", "", "unknowncodec")]
    [InlineData("opus", "opus", "Opus")]
    public void CodecProfileLabel_returns_human_readable_name(string? codec, string? profile, string expected)
    {
        Assert.Equal(expected, FormatBadges.CodecProfileLabel(codec, profile));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0L, "—")]
    [InlineData(-100L, "—")]
    [InlineData(999L, "999 bps")]
    [InlineData(1000L, "1 kbps")]
    [InlineData(640000L, "640 kbps")]
    [InlineData(1000000L, "1 Mbps")]
    [InlineData(18400000L, "18.4 Mbps")]
    [InlineData(18400000000L, "18400 Mbps")]
    public void FormatBitrate_formats_correctly(long? bps, string expected)
    {
        Assert.Equal(expected, InfoFormatters.FormatBitrate(bps));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0L, "—")]
    [InlineData(-100L, "—")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KiB")]
    [InlineData(921L, "921 B")]
    [InlineData(1048576L, "1 MiB")]
    [InlineData(4600000000L, "4.28 GiB")]
    [InlineData(2147483648L, "2 GiB")]
    [InlineData(2199023255552L, "2 TiB")]
    public void FormatBytes_formats_correctly(long? bytes, string expected)
    {
        Assert.Equal(expected, InfoFormatters.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    [InlineData("  ", "—")]
    [InlineData("matroska,webm", "matroska")]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "mov")]
    [InlineData("matroska", "matroska")]
    [InlineData("mpegts", "mpegts")]
    [InlineData("ogg", "ogg")]
    [InlineData("aac", "aac")]
    public void ShortContainer_returns_first_token(string? raw, string expected)
    {
        Assert.Equal(expected, InfoFormatters.ShortContainer(raw));
    }

    [Theory]
    [InlineData("matroska,webm", "matroska")]
    [InlineData("mov,mp4,m4a", "mov")]
    [InlineData("mpegts", "mpegts")]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    public void ContainerLabel_includes_short_form(string? raw, string expected)
    {
        Assert.Equal(expected, InfoFormatters.ContainerLabel(raw));
    }
}
