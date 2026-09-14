using Penrose.Core.Options;
using Penrose.Core.Playback;
using Penrose.Core.Sources;

namespace Penrose.Core.Tests;

/// <summary>
/// Review fixes: credentials stay same-origin, mpv.conf cannot weaken TLS or run
/// programs, and the reducer reaches Ended under keep-open.
/// </summary>
public sealed class SecurityBoundaryTests
{
    [Theory]
    [InlineData("tls-verify=no")]
    [InlineData("tls-ca-file=C:\\evil.pem")]
    [InlineData("http-header-fields=Authorization: Bearer x")]
    [InlineData("cookies=yes")]
    [InlineData("user-agent=Foo")]
    [InlineData("stream-lavf-o=tls_verify=0")]
    [InlineData("http-proxy=http://attacker:8080")]
    [InlineData("ytdl=yes")]
    [InlineData("ytdl-raw-options=exec=calc.exe")]
    [InlineData("script=C:\\x.lua")]
    [InlineData("scripts=C:\\x.lua")]
    [InlineData("load-scripts=yes")]
    [InlineData("input-conf=C:\\input.conf")]
    [InlineData("input-ipc-server=\\\\.\\pipe\\mpv")]
    [InlineData("log-file=C:\\log.txt")]
    [InlineData("dump-stats=C:\\stats.txt")]
    [InlineData("include=C:\\other.conf")]
    [InlineData("config-dir=C:\\cfg")]
    public void Conf_import_rejects_credential_tls_and_script_keys(string line)
    {
        MpvConfParseResult result = MpvConfParser.Parse(line);
        Assert.Empty(result.Accepted);
        OptionValidationResult rejected = Assert.Single(result.Rejected);
        Assert.False(rejected.Accepted);
        Assert.NotNull(rejected.RejectionReason);
    }

    [Theory]
    [InlineData("af=lavfi=[ametadata=mode=print:file=C:\\out.txt]")]
    [InlineData("vf=lavfi=[drawtext=file=x]")]
    [InlineData("ao=pcm")]
    public void Conf_import_rejects_values_that_write_files(string line)
    {
        MpvConfParseResult result = MpvConfParser.Parse(line);
        Assert.Empty(result.Accepted);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public void Conf_import_strips_trailing_comments_and_quotes_and_no_prefix()
    {
        MpvConfParseResult result = MpvConfParser.Parse(
            """
            deband=yes # banding on gradients
            scale="ewa_lanczos"
            no-interpolation
            --dither-depth=8
            [gpu-hq]
            volume=50
            [default]
            audio-channels=stereo   # after profile section
            """);

        Assert.Equal("yes", result.Accepted["deband"]);
        Assert.Equal("ewa_lanczos", result.Accepted["scale"]);
        Assert.Equal("no", result.Accepted["interpolation"]);
        Assert.Equal("8", result.Accepted["dither-depth"]);
        Assert.Equal("stereo", result.Accepted["audio-channels"]);
        Assert.False(result.Accepted.ContainsKey("volume"));
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Playback_policy_layer_no_longer_owns_network_keys()
    {
        Assert.False(OptionWhitelist.Validate(OptionLayer.PlaybackPolicy, "tls-verify").Accepted);
        Assert.False(OptionWhitelist.Validate(OptionLayer.PlaybackPolicy, "http-header-fields").Accepted);
        Assert.False(OptionWhitelist.Validate(OptionLayer.PlaybackPolicy, "cookies").Accepted);
        Assert.False(OptionWhitelist.Validate(OptionLayer.UserAdvanced, "user-agent").Accepted);
    }

    [Fact]
    public void Engine_bootstrap_disables_ytdl_and_scripts()
    {
        IReadOnlyDictionary<string, string> props = new EngineBootstrapOptions().ToProperties();
        Assert.Equal("no", props["ytdl"]);
        Assert.Equal("no", props["load-scripts"]);
    }

    [Fact]
    public void Api_key_is_only_appended_for_the_issuing_origin()
    {
        Dictionary<string, string> headers = new() { ["X-Emby-Token"] = "tok" };
        Uri server = new("https://emby.example:8920/emby/videos/1/stream.mkv");

        Uri same = HttpQueryAuth.Apply(new Uri("https://emby.example:8920/Subtitles/2/Stream.ass"), headers, server);
        Assert.Contains("api_key=tok", same.Query, StringComparison.Ordinal);

        Uri otherHost = HttpQueryAuth.Apply(new Uri("https://cdn.other/sub.ass"), headers, server);
        Assert.DoesNotContain("api_key", otherHost.Query, StringComparison.Ordinal);

        Uri otherPort = HttpQueryAuth.Apply(new Uri("https://emby.example/sub.ass"), headers, server);
        Assert.DoesNotContain("api_key", otherPort.Query, StringComparison.Ordinal);

        Uri otherScheme = HttpQueryAuth.Apply(new Uri("http://emby.example:8920/sub.ass"), headers, server);
        Assert.DoesNotContain("api_key", otherScheme.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void Http_header_fields_is_a_comma_list_with_escaped_commas()
    {
        PlaybackRequest request = new()
        {
            RequestId = Guid.NewGuid(),
            Uri = new Uri("https://emby.example/play"),
            Headers = new Dictionary<string, string>
            {
                ["X-Emby-Token"] = "tok",
                ["X-Emby-Authorization"] = "MediaBrowser Client=\"MP\", Device=\"W\"",
            },
        };

        string fields = request.ToFileLocalOptions()["http-header-fields"];
        Assert.Equal("X-Emby-Token: tok,X-Emby-Authorization: MediaBrowser Client=\"MP\"\\, Device=\"W\"", fields);
    }

    [Fact]
    public void Server_file_paths_are_not_probed_by_default()
    {
        Assert.False(DirectPlayPath.TryOpenLocal("File", @"\\attacker\share\movie.mkv", out _));
        Assert.False(DirectPlayPath.TryOpenLocal("File", @"\\attacker\share\movie.mkv", allowServerFilePaths: false, out _));
        Assert.True(DirectPlayPath.LooksLikeFile("File", @"\\nas\share\movie.mkv"));
        Assert.False(DirectPlayPath.IsHttpUrl("/mnt/media/movie.mkv"));
        Assert.True(DirectPlayPath.LooksLikeRemoteUrl("/videos/1/stream.mkv"));
    }

    [Fact]
    public void Progress_key_is_stable_across_play_sessions()
    {
        PlaybackCandidate first = new()
        {
            Method = PlayMethod.DirectStream,
            Uri = new Uri("https://emby.example/videos/1/stream.mkv?PlaySessionId=aaa&api_key=tok"),
            ItemId = "1",
            MediaSourceId = "src",
            ProviderId = "emby",
        };
        PlaybackCandidate second = first with
        {
            Uri = new Uri("https://emby.example/videos/1/stream.mkv?PlaySessionId=bbb&api_key=tok"),
        };

        Assert.Equal(first.ProgressKey, second.ProgressKey);
        Assert.DoesNotContain("api_key", first.ProgressKey, StringComparison.Ordinal);
        Assert.Equal("emby:1:src", first.ProgressKey);

        PlaybackCandidate local = first with { Uri = new Uri("file:///D:/movie.mkv"), Method = PlayMethod.DirectPlay };
        Assert.Equal("file:///D:/movie.mkv", local.ProgressKey);
    }

    [Fact]
    public void Reducer_reaches_ended_from_eof_reached_and_returns_on_seek_back()
    {
        PlaybackReducer reducer = new();
        PlaybackSnapshot s = PlaybackSnapshot.Created;
        s = reducer.Reduce(s, new EngineInitializedEvent { Generation = 0 });
        s = reducer.Reduce(s, new BeginLoadEvent { Generation = 1 });
        s = reducer.Reduce(s, new StartFileEvent { Generation = 1 });
        s = reducer.Reduce(s, new FileLoadedEvent { Generation = 1 });
        s = reducer.Reduce(s, new PositionChangedEvent { Generation = 1, Position = TimeSpan.FromSeconds(9) });

        s = reducer.Reduce(s, new EofReachedEvent { Generation = 1, Reached = true });
        Assert.Equal(MediaPhase.Ended, s.MediaPhase);
        Assert.Equal(Activity.None, s.Activity);

        s = reducer.Reduce(s, new EofReachedEvent { Generation = 1, Reached = false });
        Assert.Equal(MediaPhase.Loaded, s.MediaPhase);

        // Opening never becomes Ended from a stale flag.
        PlaybackSnapshot opening = reducer.Reduce(s, new BeginLoadEvent { Generation = 2 });
        Assert.Equal(MediaPhase.Opening, reducer.Reduce(opening, new EofReachedEvent { Generation = 2, Reached = true }).MediaPhase);
    }
}
