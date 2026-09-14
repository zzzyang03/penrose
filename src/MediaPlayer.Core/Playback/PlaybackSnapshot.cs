namespace MediaPlayer.Core.Playback;

/// <summary>
/// Immutable combination snapshot. UI and reporters subscribe to this type only;
/// they must not keep unconstrained IsPlaying / IsBuffering / IsSeeking flags.
/// </summary>
public sealed record PlaybackSnapshot
{
    public static PlaybackSnapshot Created { get; } = new()
    {
        EngineLifecycle = EngineLifecycle.Created,
        MediaPhase = MediaPhase.Empty,
        PlaybackIntent = PlaybackIntent.Paused,
        Activity = Activity.None,
        PlaybackGeneration = 0,
    };

    public EngineLifecycle EngineLifecycle { get; init; }
    public MediaPhase MediaPhase { get; init; }
    public PlaybackIntent PlaybackIntent { get; init; }
    public Activity Activity { get; init; }
    public int? BufferingPercent { get; init; }
    public TimeSpan? Position { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? Error { get; init; }
    public long PlaybackGeneration { get; init; }
    public IReadOnlyList<TrackInfo> Tracks { get; init; } = [];
    public IReadOnlyList<ChapterInfo> Chapters { get; init; } = [];
    public VideoParams? VideoParams { get; init; }
    public OutputParams? OutputParams { get; init; }
    public bool PausedForCache { get; init; }

    public bool IsFrozen =>
        EngineLifecycle is EngineLifecycle.Disposing or EngineLifecycle.Disposed;

    public IReadOnlyList<string> InvariantViolations()
    {
        List<string> violations = [];
        if (MediaPhase == MediaPhase.Empty && Activity != Activity.None)
        {
            violations.Add("MediaPhase=Empty requires Activity=None.");
        }

        if (MediaPhase == MediaPhase.Empty && Position is not null)
        {
            violations.Add("MediaPhase=Empty requires Position=null.");
        }

        if (Activity == Activity.Buffering && !PausedForCache)
        {
            violations.Add("Activity=Buffering requires paused-for-cache=true.");
        }

        return violations;
    }
}
