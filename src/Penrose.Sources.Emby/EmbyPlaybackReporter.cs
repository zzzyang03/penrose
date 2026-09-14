using Penrose.Core.Playback;
using Penrose.Core.Sources;

namespace Penrose.Sources.Emby;

/// <summary>
/// Body for <c>/Sessions/Playing</c>, <c>/Progress</c> and <c>/Stopped</c>.
/// <c>MediaSourceId</c> and <c>PlayMethod</c> let the server attribute the
/// session to the right version and count DirectPlay vs Transcode correctly.
/// </summary>
public sealed record EmbyProgressBody(
    string? ItemId,
    string? MediaSourceId,
    string? PlaySessionId,
    string? PlayMethod,
    bool CanSeek,
    long PositionTicks,
    bool IsPaused,
    string? EventName);

public static class EmbySessionPayload
{
    public static EmbyProgressBody Create(ReportingContext context, TimeSpan position, bool paused, string? eventName = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new EmbyProgressBody(
            context.ItemId,
            context.MediaSourceId,
            context.PlaySessionId,
            context.PlayMethod,
            CanSeek: true,
            // TimeSpan ticks are already 100 ns, the unit Emby uses.
            PositionTicks: Math.Max(0, position.Ticks),
            IsPaused: paused,
            EventName: eventName);
    }
}

public sealed class EmbyPlaybackReporter : IPlaybackReporter
{
    private readonly EmbyClient _client;
    private readonly string? _userId;

    public EmbyPlaybackReporter(EmbyClient client, string? userId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _userId = userId;
    }

    public Task StartAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default) =>
        _client.ReportPlayingAsync(EmbySessionPayload.Create(context, position, paused: false), cancellationToken);

    public Task ProgressAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default) =>
        _client.ReportProgressAsync(EmbySessionPayload.Create(context, position, paused: false, "TimeUpdate"), cancellationToken);

    public Task PauseAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default) =>
        _client.ReportProgressAsync(EmbySessionPayload.Create(context, position, paused: true, "Pause"), cancellationToken);

    public Task StopAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default) =>
        _client.ReportStoppedAsync(EmbySessionPayload.Create(context, position, paused: true), cancellationToken);

    public Task MarkPlayedAsync(ReportingContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(_userId) || string.IsNullOrWhiteSpace(context.ItemId))
        {
            return Task.CompletedTask;
        }

        return _client.MarkPlayedAsync(_userId, context.ItemId, cancellationToken);
    }
}
