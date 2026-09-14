using MediaPlayer.Interop.LibMpv;

namespace MediaPlayer.Playback.Mpv.Tests;

/// <summary>
/// The grabber's work is entirely blocking — creating a second mpv instance, the
/// synchronous commands, and <c>WaitEventAsync</c>, which wraps a blocking
/// <c>mpv_wait_event</c> in an already-completed task. Awaiting an uncontended
/// semaphore does not yield, so it all used to run inline on whoever called it:
/// from the seek-bar hover handler that was the UI thread, for up to eight seconds.
/// </summary>
public sealed class MpvThumbnailGrabberTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "mediaplayer-thumbs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Capture_never_runs_on_the_calling_thread()
    {
        RecordingClient client = new();
        await using MpvThumbnailGrabber grabber = new(_directory, () => client);

        // A dedicated thread, not a pool thread: it stays blocked in GetResult, so
        // the pool cannot hand the offloaded work back to it and pass by accident.
        TaskCompletionSource<CaptureRun> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread caller = new(() =>
        {
            try
            {
                int id = Environment.CurrentManagedThreadId;
                string? file = grabber
                    .CaptureAsync("C:/clip.mkv", TimeSpan.FromSeconds(12), Path.Combine(_directory, "t.jpg"))
                    .GetAwaiter()
                    .GetResult();
                done.SetResult(new CaptureRun(id, file));
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };

        caller.Start();
        CaptureRun run = await done.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Null(run.File);
        Assert.NotEqual(0, client.WaitThread);
        Assert.NotEqual(run.CallerThread, client.InitializeThread);
        Assert.NotEqual(run.CallerThread, client.CommandThread);
        Assert.NotEqual(run.CallerThread, client.WaitThread);
    }

    [Fact]
    public async Task Capture_seeks_before_taking_the_screenshot()
    {
        RecordingClient client = new();
        await using MpvThumbnailGrabber grabber = new(_directory, () => client);

        await grabber.CaptureAsync("C:/clip.mkv", TimeSpan.FromSeconds(12), Path.Combine(_directory, "t.jpg"));

        Assert.Equal(
            ["loadfile", "seek", "screenshot-to-file"],
            client.Commands.Select(args => args[0]).ToArray());
        Assert.Equal("12", client.Commands[1][1]);
        Assert.Equal("absolute+exact", client.Commands[1][2]);
        Assert.Equal("video", client.Commands[2][2]);
    }

    /// <summary>
    /// FILE_LOADED leaves the first PLAYBACK_RESTART queued. If the seek's wait
    /// consumed that stale one, the still would show the pre-seek frame and the
    /// real PLAYBACK_RESTART would still be pending when the screenshot is taken.
    /// </summary>
    [Fact]
    public async Task Capture_drains_stale_playback_restart_before_seeking()
    {
        RecordingClient client = new();
        await using MpvThumbnailGrabber grabber = new(_directory, () => client);

        await grabber.CaptureAsync("C:/clip.mkv", TimeSpan.FromSeconds(12), Path.Combine(_directory, "t.jpg"));

        Assert.Equal(0, client.PendingAtScreenshot);
    }

    [Fact]
    public async Task Grabber_uses_no_window_and_software_decode()
    {
        RecordingClient client = new();
        await using MpvThumbnailGrabber grabber = new(_directory, () => client);
        await grabber.CaptureAsync("C:/clip.mkv", TimeSpan.FromSeconds(1), Path.Combine(_directory, "t.jpg"));

        Assert.Equal("null", client.Properties["vo"]);
        Assert.Equal("no", client.Properties["force-window"]);
        Assert.Equal("no", client.Properties["hwdec"]);
        Assert.Equal("no", client.Properties["ytdl"]);
        Assert.StartsWith("scale=w=", client.Properties["vf"], StringComparison.Ordinal);
    }

    [Fact]
    public void Constructing_cleans_leftover_stills()
    {
        Directory.CreateDirectory(_directory);
        string stale = Path.Combine(_directory, "t3.jpg");
        File.WriteAllBytes(stale, [1]);
        _ = new MpvThumbnailGrabber(_directory, () => new RecordingClient());
        Assert.False(File.Exists(stale));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record CaptureRun(int CallerThread, string? File);

    /// <summary>
    /// Mirrors the real client where it matters: the waits block, and the task they
    /// hand back is already completed, so awaiting it never yields.
    /// </summary>
    private sealed class RecordingClient : IMpvClient
    {
        private readonly Queue<MpvEventId> _pending = new();

        public List<IReadOnlyList<string>> Commands { get; } = [];

        public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);

        public int InitializeThread { get; private set; }

        public int CommandThread { get; private set; }

        public int WaitThread { get; private set; }

        public int PendingAtScreenshot { get; private set; } = -1;

        public bool TerminateDestroyCalled { get; private set; }

        public void Initialize() => InitializeThread = Environment.CurrentManagedThreadId;

        public ulong NextReplyUserdata() => 1;

        /// <summary>
        /// Like libmpv: loadfile produces FILE_LOADED followed by the initial
        /// PLAYBACK_RESTART; a seek produces its own PLAYBACK_RESTART.
        /// </summary>
        public void Command(IReadOnlyList<string> args)
        {
            CommandThread = Environment.CurrentManagedThreadId;
            Commands.Add(args);
            switch (args[0])
            {
                case "loadfile":
                    _pending.Enqueue(MpvEventId.FileLoaded);
                    _pending.Enqueue(MpvEventId.PlaybackRestart);
                    break;
                case "seek":
                    _pending.Enqueue(MpvEventId.PlaybackRestart);
                    break;
                case "screenshot-to-file":
                    PendingAtScreenshot = _pending.Count;
                    break;
            }
        }

        public void CommandAsync(IReadOnlyList<string> args, ulong replyUserdata) => Command(args);

        public void SetProperty(string name, string value) => Properties[name] = value;

        public string? GetPropertyString(string name) => null;

        public long? GetPropertyInt64(string name) => null;

        public void ObserveProperty(string name, MpvFormat format)
        {
        }

        public void RequestLogMessages(string minLevel)
        {
        }

        public Task<MpvClientEvent?> WaitEventAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            WaitThread = Environment.CurrentManagedThreadId;
            if (_pending.Count == 0)
            {
                if (timeout > TimeSpan.Zero)
                {
                    Thread.Sleep(20);
                }

                return Task.FromResult<MpvClientEvent?>(null);
            }

            return Task.FromResult<MpvClientEvent?>(new MpvClientEvent(_pending.Dequeue(), 0, 0));
        }

        public Task TerminateDestroyAsync()
        {
            TerminateDestroyCalled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new(TerminateDestroyAsync());
    }
}
