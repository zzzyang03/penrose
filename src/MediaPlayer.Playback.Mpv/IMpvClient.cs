using MediaPlayer.Interop.LibMpv;

namespace MediaPlayer.Playback.Mpv;

public sealed record MpvClientEvent(
    MpvEventId Id,
    ulong ReplyUserdata,
    int Error,
    MpvEndFileReason? EndFileReason = null,
    string? ErrorString = null,
    string? PropertyName = null,
    string? PropertyString = null,
    bool? PropertyFlag = null,
    /// <summary>For <see cref="MpvEventId.LogMessage"/>: module prefix ("stream", "mkv", "ffmpeg/demuxer").</summary>
    string? LogPrefix = null,
    /// <summary>mpv level name: fatal, error, warn, info, v, debug, trace.</summary>
    string? LogLevel = null,
    string? LogText = null);

public interface IMpvClient : IAsyncDisposable
{
    bool TerminateDestroyCalled { get; }

    /// <summary>
    /// <c>mpv_initialize</c>. Options that must exist before VO creation
    /// (<c>vo</c>, <c>gpu-api</c>, <c>wid</c>, <c>d3d11-output-mode</c>)
    /// are applied via <see cref="SetProperty"/> before this call.
    /// </summary>
    void Initialize();

    ulong NextReplyUserdata();

    /// <summary>Blocking <c>mpv_command</c>. Never call this on a UI thread.</summary>
    void Command(IReadOnlyList<string> args);

    void CommandAsync(IReadOnlyList<string> args, ulong replyUserdata);

    /// <summary>
    /// Before <see cref="Initialize"/> this is <c>mpv_set_option_string</c>;
    /// afterwards <c>mpv_set_property_string</c>.
    /// </summary>
    void SetProperty(string name, string value);

    string? GetPropertyString(string name);

    /// <summary>
    /// <c>display-swapchain</c> and other INT64 properties. Null if unavailable.
    /// </summary>
    long? GetPropertyInt64(string name);

    /// <summary>
    /// <c>mpv_observe_property</c>. The format decides which field of
    /// <see cref="MpvClientEvent"/> carries the value: only
    /// <see cref="MpvFormat.Flag"/> fills <c>PropertyFlag</c>. Observing a
    /// boolean property as <see cref="MpvFormat.String"/> leaves that field null.
    /// </summary>
    void ObserveProperty(string name, MpvFormat format);

    /// <summary><c>mpv_request_log_messages</c>: deliver mpv's own log at <paramref name="minLevel"/> or above as events.</summary>
    void RequestLogMessages(string minLevel);

    Task<MpvClientEvent?> WaitEventAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Final. Must not run on StopPlaybackAsync.</summary>
    Task TerminateDestroyAsync();
}
