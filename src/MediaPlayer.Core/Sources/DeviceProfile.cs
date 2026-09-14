namespace MediaPlayer.Core.Sources;

public sealed record DeviceProfile(
    string Name,
    IReadOnlyList<DirectPlayProfile> DirectPlay,
    IReadOnlyList<TranscodeProfile> Transcode,
    IReadOnlyList<SubtitleDelivery> Subtitles,
    int MaxStreamingBitrate = 120_000_000);

public sealed record DirectPlayProfile(
    string Type,
    string Container,
    string? VideoCodec,
    string? AudioCodec);

public sealed record TranscodeProfile(
    string Type,
    string Container,
    string? VideoCodec,
    string? AudioCodec,
    string Protocol);

public sealed record SubtitleDelivery(string Format, string Method);
