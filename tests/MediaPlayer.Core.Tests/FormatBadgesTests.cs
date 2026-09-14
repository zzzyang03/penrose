using MediaPlayer.Core.Playback;

namespace MediaPlayer.Core.Tests;

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
}
