namespace Penrose.Core.Playback;

/// <summary>
/// State transferred when falling back to a second mpv instance.
/// Route A reuses the same core and does not need a second copy of this record.
/// </summary>
public sealed record PlaybackHandoffState
{
    public required Uri Uri { get; init; }
    public IReadOnlyDictionary<string, string> HttpHeaders { get; init; } =
        new Dictionary<string, string>();
    public string? Cookies { get; init; }
    public IReadOnlyList<ExternalSubtitle> ExternalSubtitles { get; init; } = [];
    public TimeSpan Position { get; init; }
    public bool PauseState { get; init; }
    public long? AudioTrackId { get; init; }
    public IReadOnlyList<long> SubtitleTrackIds { get; init; } = [];
    public long? Edition { get; init; }
    public int? Chapter { get; init; }
    public double PlaybackSpeed { get; init; } = 1.0;
    public string? AudioDevice { get; init; }
    public AudioPolicy AudioOutputMode { get; init; } = AudioPolicy.SystemCompatible;
    public string? LoopState { get; init; }
    public HdrPolicy HdrPolicy { get; init; } = HdrPolicy.AutoTarget;
    public ReportingContext? ReportingContext { get; init; }
    public long PlaybackGeneration { get; init; }
}
