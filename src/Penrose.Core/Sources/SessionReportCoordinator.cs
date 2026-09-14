using Penrose.Core.Playback;

namespace Penrose.Core.Sources;

/// <summary>
/// Session handoff: one PlaySessionId, drop stale generation, no second Start.
/// </summary>
public sealed class SessionReportCoordinator
{
    private readonly IPlaybackReporter _reporter;
    private readonly object _gate = new();
    private string? _playSessionId;
    private long _generation = -1;
    private bool _started;
    private bool _stopped;

    public SessionReportCoordinator(IPlaybackReporter reporter)
    {
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
    }

    public string? PlaySessionId
    {
        get
        {
            lock (_gate)
            {
                return _playSessionId;
            }
        }
    }

    public void Bind(string playSessionId, long generation, bool reuseSession)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playSessionId);
        lock (_gate)
        {
            if (reuseSession && string.Equals(_playSessionId, playSessionId, StringComparison.Ordinal))
            {
                _generation = generation;
                _stopped = false;
                return;
            }

            _playSessionId = playSessionId;
            _generation = generation;
            _started = false;
            _stopped = false;
        }
    }

    public Task StartAsync(ReportingContext context, TimeSpan position, long generation, CancellationToken cancellationToken = default)
    {
        if (!TryAccept(context, generation, requireStarted: false, out bool alreadyStarted))
        {
            return Task.CompletedTask;
        }

        if (alreadyStarted)
        {
            return Task.CompletedTask;
        }

        return _reporter.StartAsync(context, position, cancellationToken);
    }

    public Task ProgressAsync(ReportingContext context, TimeSpan position, long generation, CancellationToken cancellationToken = default) =>
        TryAccept(context, generation, requireStarted: true, out _)
            ? _reporter.ProgressAsync(context, position, cancellationToken)
            : Task.CompletedTask;

    public Task PauseAsync(ReportingContext context, TimeSpan position, long generation, CancellationToken cancellationToken = default) =>
        TryAccept(context, generation, requireStarted: true, out _)
            ? _reporter.PauseAsync(context, position, cancellationToken)
            : Task.CompletedTask;

    public Task StopAsync(ReportingContext context, TimeSpan position, long generation, CancellationToken cancellationToken = default)
    {
        if (!TryAccept(context, generation, requireStarted: true, out _))
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            _stopped = true;
            _started = false;
        }

        return _reporter.StopAsync(context, position, cancellationToken);
    }

    private bool TryAccept(ReportingContext context, long generation, bool requireStarted, out bool alreadyStarted)
    {
        ArgumentNullException.ThrowIfNull(context);
        alreadyStarted = false;
        lock (_gate)
        {
            if (_stopped || _playSessionId is null || generation != _generation)
            {
                return false;
            }

            if (!string.Equals(context.PlaySessionId, _playSessionId, StringComparison.Ordinal))
            {
                return false;
            }

            alreadyStarted = _started;
            if (!requireStarted && !_started)
            {
                _started = true;
            }

            if (requireStarted && !_started)
            {
                return false;
            }

            return true;
        }
    }
}
