using MediaPlayer.Core.Options;
using MediaPlayer.Core.Playback;

namespace MediaPlayer.Core.Tests;

public sealed class OptionWhitelistTests
{
    [Fact]
    public void User_cannot_set_structural_engine_keys()
    {
        OptionValidationResult result = OptionWhitelist.Validate(OptionLayer.UserAdvanced, "vo");
        Assert.False(result.Accepted);
        Assert.Contains("EngineBootstrap", result.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void User_cannot_set_surface_keys()
    {
        OptionValidationResult result = OptionWhitelist.Validate(OptionLayer.UserAdvanced, "d3d11-output-mode");
        Assert.False(result.Accepted);
        Assert.Contains("SurfaceBootstrap", result.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_keys_are_rejected_not_ignored()
    {
        OptionValidationResult result = OptionWhitelist.Validate(OptionLayer.UserAdvanced, "definitely-not-an-option");
        Assert.False(result.Accepted);
        Assert.Contains("rejected", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fullscreen_belongs_to_engine_bootstrap()
    {
        Assert.True(OptionWhitelist.Validate(OptionLayer.EngineBootstrap, "fullscreen").Accepted);
        Assert.False(OptionWhitelist.Validate(OptionLayer.UserAdvanced, "fullscreen").Accepted);
    }

    [Fact]
    public void Engine_defaults_keep_config_off()
    {
        IReadOnlyDictionary<string, string> properties = new EngineBootstrapOptions().ToProperties();
        Assert.Equal("no", properties["config"]);
        Assert.Equal("gpu-next", properties["vo"]);
        Assert.Equal("d3d11", properties["gpu-api"]);
        Assert.Equal("no", properties["force-window"]);
    }

    [Fact]
    public void Wid_is_zero_extended_not_negative()
    {
        SurfaceBootstrapOptions options = new() { Wid = unchecked((long)uint.MaxValue) };
        IReadOnlyDictionary<string, string> properties = options.ToProperties();
        Assert.Equal(uint.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), properties["wid"]);
    }

    [Fact]
    public void Playback_policy_maps_four_audio_strategies()
    {
        PlaybackPolicyOptions defaults = new();
        IReadOnlyDictionary<string, string> compat = defaults
            .WithAudioPolicy(AudioPolicy.SystemCompatible)
            .ToProperties();
        Assert.Equal("wasapi", compat["ao"]);
        Assert.Equal("auto-safe", compat["audio-channels"]);
        Assert.Equal("no", compat["audio-exclusive"]);
        Assert.Equal("", compat["audio-spdif"]);

        IReadOnlyDictionary<string, string> stereo = defaults
            .WithAudioPolicy(AudioPolicy.ForceStereo)
            .ToProperties();
        Assert.Equal("stereo", stereo["audio-channels"]);
        Assert.Equal("no", stereo["audio-exclusive"]);

        IReadOnlyDictionary<string, string> home = defaults
            .WithAudioPolicy(AudioPolicy.HomeTheaterPcm)
            .ToProperties();
        Assert.Equal("7.1,5.1,stereo", home["audio-channels"]);
        Assert.Equal("no", home["audio-exclusive"]);

        IReadOnlyDictionary<string, string> bitstream = defaults
            .WithAudioPolicy(AudioPolicy.Bitstream)
            .ToProperties();
        Assert.Equal("yes", bitstream["audio-exclusive"]);
        Assert.Equal("ac3,eac3,dts,dts-hd,truehd", bitstream["audio-spdif"]);
        Assert.Equal("", bitstream["af"]);
    }

    [Fact]
    public void Night_mode_attaches_graph_and_bitstream_clears_it()
    {
        PlaybackPolicyOptions night = new PlaybackPolicyOptions().WithNightMode(true);
        IReadOnlyDictionary<string, string> properties = night.ToProperties();
        Assert.Equal(PlaybackPolicyOptions.NightModeFilterGraph, properties["af"]);
        Assert.Equal("acompressor,dynaudnorm,alimiter", properties["af"]);

        IReadOnlyDictionary<string, string> bitstream = night
            .WithAudioPolicy(AudioPolicy.Bitstream)
            .ToProperties();
        Assert.Equal("", bitstream["af"]);
        Assert.False(night.WithAudioPolicy(AudioPolicy.Bitstream).NightMode);
    }

    [Fact]
    public void Engine_defaults_do_not_insert_enhancement_layer_filter()
    {
        IReadOnlyDictionary<string, string> properties = new EngineBootstrapOptions().ToProperties();
        Assert.False(properties.ContainsKey("vf"));
    }

    [Fact]
    public void Resume_skips_start_and_end()
    {
        Assert.False(MediaPlayer.Core.Playback.PlaybackResume.ShouldRestore(1000, 56200));
        Assert.True(MediaPlayer.Core.Playback.PlaybackResume.ShouldRestore(20000, 56200));
        Assert.False(MediaPlayer.Core.Playback.PlaybackResume.ShouldRestore(56192, 56200));
    }

    [Fact]
    public void Playback_policy_defaults_fuzzy_sidecar_subs()
    {
        IReadOnlyDictionary<string, string> properties = new PlaybackPolicyOptions().ToProperties();
        Assert.Equal("fuzzy", properties["sub-auto"]);
        Assert.Equal("字幕;Subs;subs;subtitles", properties["sub-file-paths"]);
        Assert.True(OptionWhitelist.Validate(OptionLayer.SurfaceBootstrap, "display-fps-override").Accepted);
        Assert.True(OptionWhitelist.Validate(OptionLayer.EngineBootstrap, "hwdec-codecs").Accepted);
        Assert.False(properties.ContainsKey("af"));
    }

    [Fact]
    public void Playback_request_puts_headers_in_file_local_options()
    {
        PlaybackRequest request = new()
        {
            RequestId = Guid.NewGuid(),
            Uri = new Uri("https://example.invalid/play"),
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer secret-token" },
            UserAgent = "MediaPlayer/0.1",
            Cookies = "sid=abc",
        };

        IReadOnlyDictionary<string, string> fileLocal = request.ToFileLocalOptions();
        Assert.Contains("Authorization: Bearer secret-token", fileLocal["http-header-fields"]);
        Assert.Contains("Cookie: sid=abc", fileLocal["http-header-fields"]);
        Assert.Equal("MediaPlayer/0.1", fileLocal["user-agent"]);
        Assert.DoesNotContain("secret-token", fileLocal.Keys);
    }

    [Fact]
    public void Network_open_tuning_applies_to_http_only()
    {
        PlaybackRequest remote = new() { RequestId = Guid.NewGuid(), Uri = new Uri("https://emby.example/videos/1/original.mkv") };
        IReadOnlyDictionary<string, string> options = remote.ToFileLocalOptions();
        Assert.Contains("reconnect=1", options["stream-lavf-o"], StringComparison.Ordinal);
        Assert.Equal("2000000", options["demuxer-lavf-probesize"]);
        Assert.Equal("2", options["demuxer-lavf-analyzeduration"]);
        // Never as a global property: the key stays on the file-local list.
        Assert.True(OptionWhitelist.IsNetworkCredentialKey("stream-lavf-o"));

        PlaybackRequest local = new() { RequestId = Guid.NewGuid(), Uri = new Uri("file:///D:/movie.mkv") };
        Assert.False(local.ToFileLocalOptions().ContainsKey("stream-lavf-o"));
        Assert.False(local.ToFileLocalOptions().ContainsKey("demuxer-lavf-probesize"));
    }

    [Fact]
    public void Track_picks_become_file_local_aid_and_sid()
    {
        PlaybackRequest picked = new() { RequestId = Guid.NewGuid(), Uri = new Uri("file:///D:/movie.mkv"), AudioTrack = 2, SubtitleTrack = 3 };
        IReadOnlyDictionary<string, string> options = picked.ToFileLocalOptions();
        Assert.Equal("2", options["aid"]);
        Assert.Equal("3", options["sid"]);

        PlaybackRequest none = new() { RequestId = Guid.NewGuid(), Uri = new Uri("file:///D:/movie.mkv"), SubtitleTrack = 0 };
        Assert.Equal("no", none.ToFileLocalOptions()["sid"]);
        Assert.False(none.ToFileLocalOptions().ContainsKey("aid"));

        PlaybackRequest defaults = new() { RequestId = Guid.NewGuid(), Uri = new Uri("file:///D:/movie.mkv") };
        Assert.False(defaults.ToFileLocalOptions().ContainsKey("aid"));
        Assert.False(defaults.ToFileLocalOptions().ContainsKey("sid"));
    }

    [Fact]
    public void Disc_title_is_not_the_window_title_option()
    {
        PlaybackRequest request = new()
        {
            RequestId = Guid.NewGuid(),
            Uri = new Uri("file:///D:/movie.iso"),
            DiscTitle = 3,
        };
        Assert.False(request.ToFileLocalOptions().ContainsKey("title"));
        Assert.False(request.ToFileLocalOptions().ContainsKey("disc-title"));
        Assert.Equal(3, request.DiscTitle);
        Assert.True(OptionWhitelist.Validate(OptionLayer.PlaybackPolicy, "sub-codepage").Accepted);
    }
}
