namespace MediaPlayer.Core.Playback;

public sealed record TrackInfo(
    long Id,
    string Type,
    string? Language,
    string? Title,
    bool Selected,
    bool External,
    string? Codec = null,
    string? CodecProfile = null,
    int? Channels = null,
    string? ChannelLayout = null,
    int? DolbyVisionProfile = null,
    int? Width = null,
    int? Height = null);

public sealed record ChapterInfo(int Index, string? Title, TimeSpan Start);

public sealed record VideoParams(
    int Width,
    int Height,
    string? HwdecCurrent,
    string? ColorLevels,
    string? Primaries,
    string? Gamma,
    string? SigPeak);

public sealed record OutputParams(
    string? SwapchainFormat,
    string? ColorSpace,
    string? HwdecCurrent);

public sealed record ExternalSubtitle(Uri Uri, string? Language, string? Title, string? Encoding);

public sealed record ReportingContext(
    string? ProviderId,
    string? ItemId,
    string? PlaySessionId,
    Uri? ProgressUri,
    string? MediaSourceId = null,
    string? PlayMethod = null);
