namespace Penrose.Core.Playback;

/// <summary>
/// Domain events derived from libmpv property/event observations.
/// <see cref="PlaybackReducer"/> is the only writer of <see cref="PlaybackSnapshot"/>.
/// </summary>
public abstract record PlaybackEvent
{
    public required long Generation { get; init; }

    /// <summary>
    /// Engine-scoped events apply even when generation does not match
    /// (dispose / fault). File-scoped events of an old generation are dropped.
    /// </summary>
    public virtual bool IsEngineScoped => false;
}

public sealed record EngineInitializedEvent : PlaybackEvent
{
    public override bool IsEngineScoped => true;
}

public sealed record EngineDisposingEvent : PlaybackEvent
{
    public override bool IsEngineScoped => true;
}

public sealed record EngineDisposedEvent : PlaybackEvent
{
    public override bool IsEngineScoped => true;
}

public sealed record EngineFaultedEvent : PlaybackEvent
{
    public required string Error { get; init; }
    public override bool IsEngineScoped => true;
}

public sealed record BeginLoadEvent : PlaybackEvent;

public sealed record StartFileEvent : PlaybackEvent;

public sealed record FileLoadedEvent : PlaybackEvent;

public sealed record PlaybackRestartEvent : PlaybackEvent;

public sealed record EndFileEvent : PlaybackEvent
{
    public required EndFileReason Reason { get; init; }
    public string? Error { get; init; }
}

public sealed record IdleActiveEvent : PlaybackEvent
{
    public required bool Idle { get; init; }
}

/// <summary>
/// mpv <c>eof-reached</c>. With <c>keep-open=yes</c> the core pauses at EOF and
/// never emits END_FILE, so this is the only end-of-media signal (Ended ←
/// END_FILE(eof) / eof-reached). Seeking back clears it.
/// </summary>
public sealed record EofReachedEvent : PlaybackEvent
{
    public required bool Reached { get; init; }
}

public sealed record PauseChangedEvent : PlaybackEvent
{
    public required bool Paused { get; init; }
}

public sealed record PausedForCacheEvent : PlaybackEvent
{
    public required bool PausedForCache { get; init; }
    public int? BufferingPercent { get; init; }
}

public sealed record SeekingEvent : PlaybackEvent
{
    public required bool Seeking { get; init; }
}

public sealed record VideoReconfigEvent : PlaybackEvent;

public sealed record VoConfiguredEvent : PlaybackEvent
{
    public required bool Configured { get; init; }
}

public sealed record PositionChangedEvent : PlaybackEvent
{
    public TimeSpan? Position { get; init; }
}

public sealed record DurationChangedEvent : PlaybackEvent
{
    public TimeSpan? Duration { get; init; }
}

public sealed record TracksChangedEvent : PlaybackEvent
{
    public required IReadOnlyList<TrackInfo> Tracks { get; init; }
}

public sealed record ChaptersChangedEvent : PlaybackEvent
{
    public required IReadOnlyList<ChapterInfo> Chapters { get; init; }
}

public sealed record VideoParamsChangedEvent : PlaybackEvent
{
    public VideoParams? VideoParams { get; init; }
}

public sealed record OutputParamsChangedEvent : PlaybackEvent
{
    public OutputParams? OutputParams { get; init; }
}
