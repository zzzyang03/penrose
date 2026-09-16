using System.Threading.Channels;
using Penrose.Core.Engine;
using Penrose.Core.Options;
using Penrose.Core.Playback;
using Penrose.Interop.LibMpv;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Penrose.Playback.Mpv;

public sealed class MpvPlaybackEngine : IPlaybackEngine
{
    private readonly IMpvClient _client;
    private readonly ILogger _logger;
    private readonly PlaybackReducer _reducer = new();
    private readonly EngineBootstrapOptions _engineOptions;
    private readonly SurfaceBootstrapOptions _surfaceOptions;
    private readonly PlaybackPolicyOptions _policyOptions;
    private readonly Channel<Work> _commands = Channel.CreateUnbounded<Work>();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();

    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Created;
    private long _generation;
    private long _eventGeneration;
    /// <summary>First error-level mpv log line of the current load, e.g. "curl: HTTP error 404".</summary>
    private string? _lastErrorLog;
    private TaskCompletionSource<PlaybackSnapshot>? _loadTcs;
    private TaskCompletionSource? _stopTcs;
    private Task? _eventLoop;
    private Task? _commandLoop;
    private volatile bool _accepting = true;
    private volatile bool _coreShutdown;
    private int _disposeState;
    private PlaybackRequest? _pendingRequest;

    public MpvPlaybackEngine(
        IMpvClient client,
        EngineBootstrapOptions? engineOptions = null,
        SurfaceBootstrapOptions? surfaceOptions = null,
        PlaybackPolicyOptions? policyOptions = null,
        ILogger? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _engineOptions = engineOptions ?? new EngineBootstrapOptions();
        _surfaceOptions = surfaceOptions ?? new SurfaceBootstrapOptions();
        _policyOptions = policyOptions ?? new PlaybackPolicyOptions();
        _logger = logger ?? NullLogger.Instance;
    }

    public PlaybackSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyLayer(_engineOptions.ToProperties());
        ApplyLayer(_surfaceOptions.ToProperties());

        _client.Initialize();

        ApplyLayer(_policyOptions.ToProperties());

        // Boolean properties must be observed as MPV_FORMAT_FLAG; observing them as
        // strings leaves MpvClientEvent.PropertyFlag null and silently reads false.
        _client.ObserveProperty("pause", MpvFormat.Flag);
        _client.ObserveProperty("paused-for-cache", MpvFormat.Flag);
        _client.ObserveProperty("idle-active", MpvFormat.Flag);
        _client.ObserveProperty("seeking", MpvFormat.Flag);
        _client.ObserveProperty("vo-configured", MpvFormat.Flag);
        // keep-open=yes: END_FILE is never sent at EOF, eof-reached is the signal.
        _client.ObserveProperty("eof-reached", MpvFormat.Flag);
        _client.ObserveProperty("time-pos", MpvFormat.Double);
        _client.ObserveProperty("duration", MpvFormat.Double);
        // JSON string: audio / Dolby Vision tracks often appear after FILE_LOADED
        // on a cloud strm whose header was not in the first probe window.
        _client.ObserveProperty("track-list", MpvFormat.String);
        // mpv's own warnings (and, when asked, its verbose stream / demuxer trace)
        // land in the app log instead of vanishing.
        if (!string.IsNullOrWhiteSpace(_engineOptions.MpvLogLevel) && _engineOptions.MpvLogLevel != "no")
        {
            _client.RequestLogMessages(_engineOptions.MpvLogLevel);
        }

        Apply(new EngineInitializedEvent { Generation = 0 });
        _commandLoop = Task.Run(() => CommandLoopAsync(_cts.Token));
        _eventLoop = Task.Run(() => EventLoopAsync(_cts.Token));
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<PlaybackSnapshot> LoadAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        TaskCompletionSource<PlaybackSnapshot> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Enqueue(async _ =>
        {
            _generation++;
            long generation = _generation;
            Apply(new BeginLoadEvent { Generation = generation });
            lock (_gate)
            {
                _loadTcs?.TrySetCanceled();
                _loadTcs = tcs;
                _pendingRequest = request;
            }

            try
            {
                // keep-open leaves the core paused at the previous EOF; loadfile
                // does not reset that. HTTP resume stays paused until FILE_LOADED
                // so the first frames are not from t=0 (start= is not on the load).
                _client.SetProperty("pause", request.SeekAfterOpen ? "yes" : "no");
                IReadOnlyList<string> args = LoadfileOptions.BuildCommand(request);
                _client.CommandAsync(args, _client.NextReplyUserdata());
            }
            catch (Exception ex)
            {
                // mpv rejected the command synchronously (InvalidParameter etc.).
                // Without this the snapshot stays Opening forever.
                Apply(new EndFileEvent
                {
                    Generation = generation,
                    Reason = EndFileReason.Error,
                    Error = ex.Message,
                });
                lock (_gate)
                {
                    _loadTcs?.TrySetException(ex);
                }

                throw;
            }
        }, tcs.Task, cancellationToken).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        Enqueue(_ =>
        {
            LoadfileOptions.ThrowIfGlobalNetworkWrite("pause");
            _client.SetProperty("pause", "yes");
            return Task.CompletedTask;
        }, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        Enqueue(_ =>
        {
            _client.SetProperty("pause", "no");
            return Task.CompletedTask;
        }, cancellationToken);

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) =>
        Enqueue(_ =>
        {
            _client.CommandAsync(
                ["seek", position.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute"],
                _client.NextReplyUserdata());
            return Task.CompletedTask;
        }, cancellationToken);

    public Task StopPlaybackAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return Enqueue(async _ =>
        {
            lock (_gate)
            {
                _stopTcs?.TrySetCanceled();
                _stopTcs = tcs;
                // Nothing loaded: mpv emits neither END_FILE nor an idle-active
                // change, so the completion would never arrive.
                if (_snapshot.MediaPhase == MediaPhase.Empty)
                {
                    tcs.TrySetResult();
                    return;
                }
            }

            _client.CommandAsync(["stop"], _client.NextReplyUserdata());
        }, tcs.Task, cancellationToken);
    }

    public async Task CloseMediaAsync(CancellationToken cancellationToken = default)
    {
        await StopPlaybackAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task DetachSurfaceAsync(CancellationToken cancellationToken = default) =>
        Enqueue(_ => Task.CompletedTask, cancellationToken);

    public Task ApplyPropertiesAsync(
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return Enqueue(_ =>
        {
            ApplyLayer(properties);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task<string?> GetPropertyStringAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TaskCompletionSource<string?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Enqueue(_ =>
        {
            tcs.TrySetResult(_client.GetPropertyString(name));
            return Task.CompletedTask;
        }, tcs.Task, cancellationToken).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<long?> GetPropertyInt64Async(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TaskCompletionSource<long?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Enqueue(_ =>
        {
            tcs.TrySetResult(_client.GetPropertyInt64(name));
            return Task.CompletedTask;
        }, tcs.Task, cancellationToken).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public Task ExecuteCommandAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Enqueue(_ =>
        {
            _client.CommandAsync(args, _client.NextReplyUserdata());
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _accepting = false;
        SafeApply(new EngineDisposingEvent { Generation = _generation });
        lock (_gate)
        {
            _loadTcs?.TrySetCanceled();
            _stopTcs?.TrySetCanceled();
        }

        // Complete the writer before cancelling. Cancelling first makes ReadAllAsync
        // throw while work is still queued, and every caller awaiting that work's
        // completion source would then wait forever. Completing drains the queue.
        _commands.Writer.TryComplete();
        if (_commandLoop is not null)
        {
            await AwaitQuietlyAsync(_commandLoop, "command loop").ConfigureAwait(false);
        }

        _cts.Cancel();
        if (_eventLoop is not null)
        {
            await AwaitQuietlyAsync(_eventLoop, "event loop").ConfigureAwait(false);
        }

        // Whatever happened above, the core must still be torn down; otherwise the
        // SafeHandle finalizer runs mpv_terminate_destroy on the finalizer thread.
        try
        {
            await _client.TerminateDestroyAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mpv_terminate_destroy failed");
        }

        SafeApply(new EngineDisposedEvent { Generation = _generation });
        _cts.Dispose();
    }

    private async Task AwaitQuietlyAsync(Task loop, string name)
    {
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mpv {Loop} ended with an error", name);
        }
    }

    private Task Enqueue(Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
        Enqueue(work, Task.CompletedTask, cancellationToken);

    private async Task Enqueue(Func<CancellationToken, Task> work, Task completion, CancellationToken cancellationToken)
    {
        if (!_accepting)
        {
            throw new ObjectDisposedException(nameof(MpvPlaybackEngine));
        }

        Work item = new(work, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        await _commands.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        await item.Done.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CommandLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (Work work in _commands.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await work.Action(cancellationToken).ConfigureAwait(false);
                    work.Done.TrySetResult();
                }
                catch (Exception ex)
                {
                    work.Done.TrySetException(ex);
                }
            }
        }
        finally
        {
            // Whatever is still queued will never run. Leaving those completion
            // sources unset hangs whoever is awaiting Enqueue.
            while (_commands.Reader.TryRead(out Work? pending))
            {
                pending.Done.TrySetCanceled();
            }
        }
    }

    private async Task EventLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_coreShutdown)
        {
            MpvClientEvent? evt = await _client.WaitEventAsync(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
            if (evt is null)
            {
                continue;
            }

            try
            {
                HandleClientEvent(evt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One bad event must not kill event processing for the rest of the
                // session: every pending LoadAsync/StopPlaybackAsync would hang.
                Fault("Event handling failed for " + evt.Id + ": " + ex.Message, ex);
            }
        }
    }

    private void HandleClientEvent(MpvClientEvent evt)
    {
        switch (evt.Id)
        {
            case MpvEventId.Shutdown:
                _coreShutdown = true;
                Fault("mpv core shut down.", null);
                return;
            case MpvEventId.CommandReply when evt.Error < 0:
                _logger.LogWarning(
                    "mpv command (reply {Reply}) failed: {Error}",
                    evt.ReplyUserdata,
                    evt.ErrorString ?? ((MpvError)evt.Error).ToString());
                return;
            case MpvEventId.QueueOverflow:
                _logger.LogWarning("mpv event queue overflowed; events were dropped");
                return;
            case MpvEventId.LogMessage:
                if (!string.IsNullOrEmpty(evt.LogText))
                {
                    switch (evt.LogLevel)
                    {
                        case "fatal" or "error":
                            _logger.LogError("mpv[{Prefix}] {Text}", evt.LogPrefix, evt.LogText);
                            // The first error of a load is the root cause; later ones are consequences.
                            _lastErrorLog ??= evt.LogPrefix + ": " + evt.LogText;
                            break;
                        case "warn":
                            _logger.LogWarning("mpv[{Prefix}] {Text}", evt.LogPrefix, evt.LogText);
                            break;
                        default:
                            _logger.LogInformation("mpv[{Prefix}] {Text}", evt.LogPrefix, evt.LogText);
                            break;
                    }
                }

                return;
        }

        long stamp;
        lock (_gate)
        {
            if (evt.Id == MpvEventId.StartFile)
            {
                _eventGeneration = _generation;
                _lastErrorLog = null;
            }

            stamp = _eventGeneration;
        }

        PlaybackEvent? mapped = MpvEventMapper.ToPlaybackEvent(evt, stamp);
        if (mapped is not null)
        {
            Apply(mapped);
        }

        PlaybackSnapshot snapshot = Snapshot;
        if (mapped is FileLoadedEvent fileLoaded && fileLoaded.Generation == snapshot.PlaybackGeneration)
        {
            string? pause = _client.GetPropertyString("pause");
            Apply(new PauseChangedEvent
            {
                Generation = stamp,
                Paused = pause is "yes" or "true",
            });
            AttachExternalSubtitles();
            ApplyDiscTitle();
            ApplyTracksFromClient(stamp);
            if (BeginHttpResumeIfNeeded(stamp))
            {
                return;
            }

            snapshot = Snapshot;
            lock (_gate)
            {
                _loadTcs?.TrySetResult(snapshot);
            }
        }

        if (mapped is EndFileEvent endFile && endFile.Generation == snapshot.PlaybackGeneration)
        {
            lock (_gate)
            {
                if (endFile.Reason == EndFileReason.Error)
                {
                    // mpv's own reason ("loading failed") says little; the last error
                    // it logged ("curl: HTTP error 404") says what actually happened.
                    string reason = endFile.Error ?? "load failed";
                    if (_lastErrorLog is { } detail)
                    {
                        reason += " (" + detail + ")";
                    }

                    _loadTcs?.TrySetException(new InvalidOperationException(reason));
                }
                else
                {
                    // A load that ends (stop / eof / redirect) before FILE_LOADED is
                    // over; nobody else will complete it.
                    _loadTcs?.TrySetCanceled();
                }

                _stopTcs?.TrySetResult();
            }
        }

        if (mapped is IdleActiveEvent { Idle: true } && snapshot.MediaPhase == MediaPhase.Empty)
        {
            lock (_gate)
            {
                _stopTcs?.TrySetResult();
            }
        }
    }

    private void Fault(string message, Exception? exception)
    {
        _logger.LogError(exception, "mpv engine fault: {Message}", message);
        SafeApply(new EngineFaultedEvent { Generation = _generation, Error = message });
        lock (_gate)
        {
            _loadTcs?.TrySetException(new InvalidOperationException(message, exception));
            _stopTcs?.TrySetException(new InvalidOperationException(message, exception));
        }
    }

    private void ApplyLayer(IReadOnlyDictionary<string, string> properties)
    {
        foreach ((string key, string value) in properties)
        {
            LoadfileOptions.ThrowIfGlobalNetworkWrite(key);
            _client.SetProperty(key, value);
        }
    }

    private void AttachExternalSubtitles()
    {
        PlaybackRequest? request;
        lock (_gate)
        {
            request = _pendingRequest;
        }

        if (request is null)
        {
            return;
        }

        foreach (ExternalSubtitle subtitle in request.ExternalSubtitles)
        {
            if (!TrySubtitleUrl(subtitle.Uri, request.Headers, request.Uri, out string url))
            {
                continue;
            }

            // sub-add <url> [<flags> [<title> [<lang>]]] is positional: a language
            // without a title must not slide into the title slot.
            List<string> args = ["sub-add", url, "auto"];
            if (!string.IsNullOrWhiteSpace(subtitle.Title) || !string.IsNullOrWhiteSpace(subtitle.Language))
            {
                args.Add(string.IsNullOrWhiteSpace(subtitle.Title) ? subtitle.Language ?? "" : subtitle.Title);
            }

            if (!string.IsNullOrWhiteSpace(subtitle.Language))
            {
                args.Add(subtitle.Language);
            }

            _client.CommandAsync(args, _client.NextReplyUserdata());
        }
    }

    private static bool TrySubtitleUrl(
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        Uri mediaUri,
        out string url)
    {
        url = "";
        if (uri.IsFile)
        {
            if (!HttpQueryAuth.IsReadableFile(uri))
            {
                return false;
            }

            url = uri.AbsoluteUri;
            return true;
        }

        // The token belongs to the server that issued the media URL; a subtitle on
        // any other host must not receive it.
        Uri authorized = HttpQueryAuth.Apply(uri, headers, mediaUri);
        url = authorized.AbsoluteUri;
        return true;
    }

    private void ApplyDiscTitle()
    {
        PlaybackRequest? request;
        lock (_gate)
        {
            request = _pendingRequest;
        }

        if (request?.DiscTitle is not { } title || title <= 0)
        {
            return;
        }

        _client.SetProperty(
            "disc-title",
            title.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// HTTP resume must init WASAPI (and TrueHD SPDIF) on a valid access unit at
    /// t=0. Seeking while still paused lands mid-frame and exclusive init fails,
    /// which the host then reports as bitstream→PCM stereo.
    /// </summary>
    private bool BeginHttpResumeIfNeeded(long generation)
    {
        PlaybackRequest? request;
        TaskCompletionSource<PlaybackSnapshot>? tcs;
        lock (_gate)
        {
            request = _pendingRequest;
            tcs = _loadTcs;
        }

        if (request is null || !request.SeekAfterOpen || request.StartPosition is null || tcs is null)
        {
            return false;
        }

        _ = FinishHttpResumeAsync(request, generation, tcs);
        return true;
    }

    private async Task FinishHttpResumeAsync(
        PlaybackRequest request,
        long generation,
        TaskCompletionSource<PlaybackSnapshot> tcs)
    {
        string restoreMute = "no";
        try
        {
            await Enqueue(_ =>
            {
                if (generation != _generation)
                {
                    return Task.CompletedTask;
                }

                string? current = _client.GetPropertyString("mute");
                restoreMute = current is "yes" or "true" ? "yes" : "no";
                _client.SetProperty("mute", "yes");
                _client.SetProperty("pause", "no");
                return Task.CompletedTask;
            }, CancellationToken.None).ConfigureAwait(false);

            // Exclusive TrueHD often fails instantly on HTTP (no AU yet). Do not
            // seek while exclusive is still armed: that re-inits WASAPI mid-stream
            // and TrueHD skips frames. Drop to shared PCM first, then seek.
            await WaitForAudioOutAsync(generation, TimeSpan.FromMilliseconds(800)).ConfigureAwait(false);
            await DropExclusiveIfBitstreamFailedAsync(generation).ConfigureAwait(false);
            if (generation != _generation)
            {
                return;
            }

            TimeSpan start = request.StartPosition ?? TimeSpan.Zero;
            await SeekAsync(start, CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("Play: resume seek after open start={Start}", start);
            await WaitForAudioOutAsync(generation, TimeSpan.FromMilliseconds(800)).ConfigureAwait(false);
            await DropExclusiveIfBitstreamFailedAsync(generation).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP resume seek failed");
        }
        finally
        {
            try
            {
                await Enqueue(_ =>
                {
                    if (generation != _generation)
                    {
                        return Task.CompletedTask;
                    }

                    _client.SetProperty("mute", restoreMute);
                    _client.SetProperty("pause", "no");
                    return Task.CompletedTask;
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HTTP resume unmute failed");
            }

            if (generation == _generation)
            {
                lock (_gate)
                {
                    tcs.TrySetResult(Snapshot);
                }
            }
        }
    }

    private async Task WaitForAudioOutAsync(long generation, TimeSpan budget)
    {
        DateTime deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (generation != _generation)
            {
                return;
            }

            string? format = await GetPropertyStringAsync("audio-out-params/format", CancellationToken.None)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(format))
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// If HTTP resume did not get an SPDIF AO, exclusive WASAPI must come down
    /// before the resume seek. Exclusive PCM typically opens stereo only.
    /// </summary>
    private async Task DropExclusiveIfBitstreamFailedAsync(long generation)
    {
        if (generation != _generation)
        {
            return;
        }

        string? format = await GetPropertyStringAsync("audio-out-params/format", CancellationToken.None)
            .ConfigureAwait(false);
        if (AudioPassthrough.IsSpdifFormat(format))
        {
            return;
        }

        string? exclusive = await GetPropertyStringAsync("audio-exclusive", CancellationToken.None)
            .ConfigureAwait(false);
        if (exclusive is not "yes")
        {
            return;
        }

        await Enqueue(_ =>
        {
            if (generation != _generation)
            {
                return Task.CompletedTask;
            }

            _client.SetProperty("audio-exclusive", "no");
            _client.SetProperty("audio-spdif", "");
            return Task.CompletedTask;
        }, CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation("Play: HTTP resume dropped exclusive format={Format}", format);
        if (!string.IsNullOrWhiteSpace(format))
        {
            // Stale exclusive-PCM params would make WaitForAudioOut return immediately.
            await Task.Delay(400).ConfigureAwait(false);
        }

        await WaitForAudioOutAsync(generation, TimeSpan.FromMilliseconds(800)).ConfigureAwait(false);
    }

    private void ApplyTracksFromClient(long generation)
    {
        IReadOnlyList<TrackInfo> tracks = TrackListParser.Parse(_client.GetPropertyString("track-list"));
        Apply(new TracksChangedEvent { Generation = generation, Tracks = tracks });
    }

    private void Apply(PlaybackEvent evt)
    {
        PlaybackSnapshot next;
        lock (_gate)
        {
            next = _reducer.Reduce(_snapshot, evt);
            _snapshot = next;
        }

        SnapshotChanged?.Invoke(this, next);
    }

    /// <summary>Apply on paths that must not throw (fault / dispose).</summary>
    private void SafeApply(PlaybackEvent evt)
    {
        try
        {
            Apply(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot update failed for {Event}", evt.GetType().Name);
        }
    }

    private sealed record Work(Func<CancellationToken, Task> Action, TaskCompletionSource Done);
}
