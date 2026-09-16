using System.Threading.Channels;
using Penrose.Core.Options;
using Penrose.Core.Playback;
using Penrose.Interop.LibMpv;
using Penrose.Playback.Mpv;

namespace Penrose.Playback.Mpv.Tests;

public sealed class MpvPlaybackEngineTests
{
    [Fact]
    public async Task Load_completes_on_file_loaded_not_on_loadfile_return()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(Request("https://example.invalid/a.mkv"));
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(load.IsCompleted);

        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        PlaybackSnapshot snapshot = await load.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MediaPhase.Loaded, snapshot.MediaPhase);
        Assert.Equal(1, snapshot.PlaybackGeneration);
    }

    [Fact]
    public async Task Network_headers_are_file_local_not_global_properties()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        PlaybackRequest request = Request(
            "https://example.invalid/play",
            headers: new Dictionary<string, string> { ["Authorization"] = "Bearer secret-token" },
            userAgent: "Penrose/0.1");

        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(request);
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        await load.WaitAsync(TimeSpan.FromSeconds(2));

        IReadOnlyList<string> loadfile = Assert.Single(harness.Client.Commands, c => c[0] == "loadfile");
        // mpv >= 0.38: loadfile <url> <flags> <index> <options>; index must be -1.
        Assert.Equal(5, loadfile.Count);
        Assert.Equal("replace", loadfile[2]);
        Assert.Equal("-1", loadfile[3]);
        Assert.Contains("http-header-fields=%", loadfile[4], StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer secret-token", loadfile[4], StringComparison.Ordinal);
        Assert.Contains("user-agent=%11%Penrose/0.1", loadfile[4], StringComparison.Ordinal);
        Assert.False(harness.Client.Properties.ContainsKey("http-header-fields"));
        Assert.False(harness.Client.Properties.ContainsKey("user-agent"));
        Assert.False(harness.Client.Properties.ContainsKey("cookies"));
        // Every load starts unpaused even if keep-open left the core paused at EOF.
        Assert.Equal("no", harness.Client.Properties["pause"]);
    }

    [Fact]
    public async Task Plain_load_has_no_index_or_options_arguments()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(Request("file:///tmp/a.mkv"));
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        IReadOnlyList<string> loadfile = Assert.Single(harness.Client.Commands, c => c[0] == "loadfile");
        Assert.Equal(["loadfile", "/tmp/a.mkv", "replace"], loadfile);
        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        await load.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Start_position_is_a_file_local_option_with_index_placeholder()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        PlaybackRequest request = Request("file:///tmp/a.mkv") with { StartPosition = TimeSpan.FromSeconds(90.5) };
        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(request);
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        IReadOnlyList<string> loadfile = Assert.Single(harness.Client.Commands, c => c[0] == "loadfile");
        Assert.Equal(["loadfile", "/tmp/a.mkv", "replace", "-1", "start=%4%90.5"], loadfile);
        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        await load.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("truehd")]
    [InlineData("eac3")]
    public async Task Refused_bitstream_switches_to_shared_pcm(string codec)
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.LoadAsync();
        harness.Client.Properties["audio-exclusive"] = "yes";
        harness.Client.Properties["audio-spdif"] = AudioPassthrough.SpdifCodecs;
        harness.Client.Properties["audio-codec-name"] = codec;

        PushAoInitFailure(harness);

        await WaitForPropertyAsync(harness, "audio-spdif", "");
        Assert.Equal("no", harness.Client.Properties["audio-exclusive"]);
    }

    [Theory]
    [InlineData("aac", "ac3,eac3,dts,dts-hd,truehd")]
    [InlineData("truehd", "")]
    public async Task Ao_failure_without_bitstream_leaves_audio_options_alone(string codec, string spdif)
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.LoadAsync();
        harness.Client.Properties["audio-exclusive"] = "yes";
        harness.Client.Properties["audio-spdif"] = spdif;
        harness.Client.Properties["audio-codec-name"] = codec;

        PushAoInitFailure(harness);
        // Processed after the log line, so the log has been handled once this lands.
        harness.PushProperty("pause", flag: true);
        await harness.WaitForSnapshotAsync(s => s.PlaybackIntent == PlaybackIntent.Paused);

        Assert.Equal("yes", harness.Client.Properties["audio-exclusive"]);
        Assert.Equal(spdif, harness.Client.Properties["audio-spdif"]);
    }

    private static void PushAoInitFailure(EngineHarness harness) =>
        harness.Client.Push(new MpvClientEvent(
            MpvEventId.LogMessage,
            ReplyUserdata: 0,
            Error: 0,
            LogPrefix: "ao",
            LogLevel: "error",
            LogText: "Failed to initialize audio driver 'wasapi'"));

    private static async Task WaitForPropertyAsync(EngineHarness harness, string name, string expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (harness.Client.Properties.TryGetValue(name, out string? value) && value == expected)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"{name} never became '{expected}'");
    }

    [Fact]
    public async Task Eof_reached_with_keep_open_maps_to_ended_and_seeking_back_restores_loaded()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.LoadAsync();
        harness.PushProperty("eof-reached", flag: true);
        PlaybackSnapshot ended = await harness.WaitForSnapshotAsync(s => s.MediaPhase == MediaPhase.Ended);
        Assert.Equal(Activity.None, ended.Activity);

        harness.PushProperty("eof-reached", flag: false);
        await harness.WaitForSnapshotAsync(s => s.MediaPhase == MediaPhase.Loaded);
    }

    [Fact]
    public async Task Event_handler_exception_faults_engine_but_keeps_loop_alive()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        int calls = 0;
        harness.Engine.SnapshotChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("subscriber blew up");
            }
        };

        harness.PushProperty("pause", flag: true);
        await harness.WaitForSnapshotAsync(s => s.EngineLifecycle == EngineLifecycle.Faulted);

        // The loop is still processing events afterwards.
        harness.PushProperty("pause", flag: false);
        await harness.WaitForSnapshotAsync(s => s.PlaybackIntent == PlaybackIntent.Playing);
    }

    [Fact]
    public async Task Stop_with_nothing_loaded_completes_immediately()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.Engine.StopPlaybackAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(harness.Client.Commands, c => c[0] == "stop");
    }

    [Fact]
    public async Task Load_rejected_synchronously_by_mpv_fails_and_marks_media_failed()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        harness.Client.FailNextCommand = new MpvException(MpvError.InvalidParameter, "mpv_command_async loadfile");
        await Assert.ThrowsAsync<MpvException>(() =>
            harness.Engine.LoadAsync(Request("file:///tmp/a.mkv")).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(MediaPhase.Failed, harness.Engine.Snapshot.MediaPhase);
    }

    [Fact]
    public async Task Shutdown_event_faults_engine_and_fails_pending_load()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(Request("file:///tmp/a.mkv"));
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        harness.Client.Push(new MpvClientEvent(MpvEventId.Shutdown, 0, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => load.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(EngineLifecycle.Faulted, harness.Engine.Snapshot.EngineLifecycle);
    }

    [Fact]
    public async Task Double_dispose_is_a_no_op()
    {
        EngineHarness harness = await EngineHarness.StartAsync();
        await harness.Engine.DisposeAsync();
        await harness.Engine.DisposeAsync();
        Assert.Equal(EngineLifecycle.Disposed, harness.Engine.Snapshot.EngineLifecycle);
    }

    [Fact]
    public async Task Stop_does_not_terminate_destroy()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(Request("file:///tmp/a.mkv"));
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        await load.WaitAsync(TimeSpan.FromSeconds(2));

        Task stop = harness.Engine.StopPlaybackAsync();
        await harness.Client.StopIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        harness.Client.Push(new MpvClientEvent(
            MpvEventId.EndFile, 2, 0, EndFileReason: MpvEndFileReason.Stop));
        await stop.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(harness.Client.TerminateDestroyCalled);
        Assert.Equal(EngineLifecycle.Initialized, harness.Engine.Snapshot.EngineLifecycle);
        Assert.Equal(MediaPhase.Empty, harness.Engine.Snapshot.MediaPhase);
    }

    [Fact]
    public async Task Dispose_is_the_only_terminate_destroy()
    {
        EngineHarness harness = await EngineHarness.StartAsync();
        await harness.Engine.DisposeAsync();
        Assert.True(harness.Client.TerminateDestroyCalled);
        Assert.Equal(EngineLifecycle.Disposed, harness.Engine.Snapshot.EngineLifecycle);
    }

    [Fact]
    public async Task Stale_file_loaded_does_not_complete_new_load()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        Task<PlaybackSnapshot> first = harness.Engine.LoadAsync(Request("file:///tmp/a.mkv"));
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        await first.WaitAsync(TimeSpan.FromSeconds(2));

        FakeMpvClient client = harness.Client;
        client.LoadfileIssued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PlaybackSnapshot> second = harness.Engine.LoadAsync(Request("file:///tmp/b.mkv"));
        await client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(second.IsCompleted);

        client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 9, 0));
        await Task.Delay(80);
        Assert.False(second.IsCompleted);

        client.Push(new MpvClientEvent(MpvEventId.StartFile, 2, 0));
        client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 2, 0));
        PlaybackSnapshot loaded = await second.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, loaded.PlaybackGeneration);
    }

    [Fact]
    public async Task Engine_bootstrap_keeps_config_no()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        Assert.Equal("no", harness.Client.Properties["config"]);
        Assert.Equal("gpu-next", harness.Client.Properties["vo"]);
        Assert.False(harness.Client.Properties.ContainsKey("vf"));
        Assert.Equal("fuzzy", harness.Client.Properties["sub-auto"]);
    }

    [Fact]
    public async Task Surface_and_policy_layers_apply_around_initialize()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync(
            surface: new SurfaceBootstrapOptions
            {
                D3d11OutputMode = "composition",
                D3d11OutputFormat = "rgba16f",
                D3d11OutputCsp = "linear",
            },
            policy: new PlaybackPolicyOptions().WithNightMode(true));
        Assert.Equal("composition", harness.Client.Properties["d3d11-output-mode"]);
        Assert.Equal("rgba16f", harness.Client.Properties["d3d11-output-format"]);
        Assert.Equal(PlaybackPolicyOptions.NightModeFilterGraph, harness.Client.Properties["af"]);
    }

    [Fact]
    public async Task Apply_properties_can_switch_output_mode()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.Engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["d3d11-output-mode"] = "window",
            ["fullscreen"] = "yes",
        });
        Assert.Equal("window", harness.Client.Properties["d3d11-output-mode"]);
        Assert.Equal("yes", harness.Client.Properties["fullscreen"]);
    }

    [Fact]
    public async Task External_subtitles_are_sub_added_after_file_loaded()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        string sub = Path.Combine(Path.GetTempPath(), "mp-sub-" + Guid.NewGuid().ToString("N") + ".ass");
        File.WriteAllText(sub, "[Script Info]\n");
        try
        {
            Uri subUri = new(Path.GetFullPath(sub));
            PlaybackRequest request = Request("file:///tmp/a.mkv") with
            {
                ExternalSubtitles =
                [
                    new ExternalSubtitle(subUri, "zh", "chs", null),
                ],
            };
            Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(request);
            await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
            harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
            harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
            await load.WaitAsync(TimeSpan.FromSeconds(2));

            IReadOnlyList<string> subAdd = Assert.Single(harness.Client.Commands, c => c[0] == "sub-add");
            Assert.Equal(subUri.AbsoluteUri, subAdd[1]);
            Assert.Equal("auto", subAdd[2]);
            Assert.Equal("chs", subAdd[3]);
            Assert.Equal("zh", subAdd[4]);
        }
        finally
        {
            File.Delete(sub);
        }
    }

    [Fact]
    public async Task Http_subtitles_append_api_key_from_request_headers()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        PlaybackRequest request = Request(
            "https://emby.example/play",
            headers: new Dictionary<string, string> { ["X-Emby-Token"] = "secret-token" }) with
        {
            ExternalSubtitles =
            [
                new ExternalSubtitle(
                    new Uri("https://emby.example/Items/1/Subtitles/2/Stream.ass"),
                    "zh",
                    "chs",
                    null),
            ],
        };
        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(request);
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        await load.WaitAsync(TimeSpan.FromSeconds(2));

        IReadOnlyList<string> subAdd = Assert.Single(harness.Client.Commands, c => c[0] == "sub-add");
        Assert.Contains("api_key=secret-token", subAdd[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_file_subtitles_are_not_sub_added()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        string missing = Path.Combine(Path.GetTempPath(), "mp-missing-" + Guid.NewGuid().ToString("N") + ".ass");
        PlaybackRequest request = Request("file:///tmp/a.mkv") with
        {
            ExternalSubtitles =
            [
                new ExternalSubtitle(new Uri(Path.GetFullPath(missing)), "zh", "chs", null),
            ],
        };
        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(request);
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        await load.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(harness.Client.Commands, c => c[0] == "sub-add");
    }

    [Fact]
    public async Task Disc_title_is_set_as_property_after_file_loaded()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        PlaybackRequest request = Request("file:///D:/movie.iso") with { DiscTitle = 3 };
        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(request);
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        IReadOnlyList<string> loadfile = Assert.Single(harness.Client.Commands, c => c[0] == "loadfile");
        Assert.DoesNotContain(loadfile, arg => arg.Contains("title=", StringComparison.Ordinal));
        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        await load.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("3", harness.Client.Properties["disc-title"]);
    }

    [Fact]
    public async Task File_loaded_fills_track_list_from_mpv_json()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        harness.Client.Properties["track-list"] =
            """[{"id":1,"type":"audio","lang":"jpn","title":"","selected":true,"external":false}]""";
        Task<PlaybackSnapshot> load = harness.Engine.LoadAsync(Request("file:///tmp/a.mkv"));
        await harness.Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        harness.Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        harness.Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        PlaybackSnapshot snapshot = await load.WaitAsync(TimeSpan.FromSeconds(2));
        TrackInfo track = Assert.Single(snapshot.Tracks);
        Assert.Equal("audio", track.Type);
        Assert.Equal("jpn", track.Language);
        Assert.True(track.Selected);
    }

    private static PlaybackRequest Request(
        string uri,
        IReadOnlyDictionary<string, string>? headers = null,
        string? userAgent = null) =>
        new()
        {
            RequestId = Guid.NewGuid(),
            Uri = new Uri(uri),
            Headers = headers ?? new Dictionary<string, string>(),
            UserAgent = userAgent,
        };

    [Fact]
    public async Task Dispose_drains_work_that_was_already_queued()
    {
        EngineHarness harness = await EngineHarness.StartAsync();
        using ManualResetEventSlim block = new(initialState: false);
        harness.Client.CommandBlock = block;

        // Occupies the command loop, so the pause below can only sit in the queue.
        Task blocked = harness.Engine.ExecuteCommandAsync(["screenshot"]);
        await Task.Delay(100);
        Task queued = harness.Engine.PauseAsync();
        await Task.Delay(100);
        Assert.False(queued.IsCompleted);

        ValueTask dispose = harness.Engine.DisposeAsync();
        block.Set();

        await dispose.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await blocked.WaitAsync(TimeSpan.FromSeconds(5));
        await queued.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("yes", harness.Client.Properties["pause"]);
    }

    /// <summary>
    /// Disposing races every caller sitting between the accepting check and its
    /// write. Cancelling the loop token before completing the writer could drop a
    /// work item whose completion source was never set, and its caller — awaiting
    /// with its own token, usually none — then waited forever. Every outcome is
    /// fine here except not finishing.
    /// </summary>
    [Fact]
    public async Task Dispose_never_strands_a_concurrent_caller()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            EngineHarness harness = await EngineHarness.StartAsync();
            Task[] callers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await harness.Engine.PauseAsync();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (ChannelClosedException)
                {
                }
                catch (OperationCanceledException)
                {
                }
            })).ToArray();

            await harness.Engine.DisposeAsync();
            await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
