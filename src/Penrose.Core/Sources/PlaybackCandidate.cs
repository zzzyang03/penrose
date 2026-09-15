using Penrose.Core.Playback;

namespace Penrose.Core.Sources;

/// <summary>
/// What kind of medium is feeding this candidate. The factory or media-source
/// parser decides once; the UI surfaces it in the playback info overlay so
/// "本地" / "strm 中继" / "服务器转码" can be told apart. A missing value is
/// <see cref="Unknown"/>; the field defaults to that so old call sites keep
/// working.
/// </summary>
public enum MediaSourceKind
{
    Unknown,
    LocalFile,
    LocalDisc,
    NetworkShare,
    StrmDirect,
    StrmRelay,
    ServerDirectPlay,
    ServerTranscode,
}

public enum PlayMethod
{
    DirectPlay,
    DirectStream,
    Transcode,
}

public sealed record PlaybackCandidate
{
    public required PlayMethod Method { get; init; }

    public required Uri Uri { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? Expiration { get; init; }

    public string? PlaySessionId { get; init; }

    public string? MediaSourceId { get; init; }

    public string? ItemId { get; init; }

    public string? ProviderId { get; init; }

    public IReadOnlyList<ExternalSubtitle> ExternalSubtitles { get; init; } = [];

    public string? ServerPath { get; init; }

    public bool SupportsPathMapping { get; init; }

    /// <summary>
    /// User-facing playback kind. Defaults to <see cref="MediaSourceKind.Unknown"/>
    /// so older call sites keep compiling; the local factory and the
    /// Emby/Jellyfin playback-info parsers fill it in.
    /// </summary>
    public MediaSourceKind SourceKind { get; init; } = MediaSourceKind.Unknown;

    public PlaybackRequest ToPlaybackRequest(Guid requestId) =>
        new()
        {
            RequestId = requestId,
            Uri = Uri,
            Headers = Headers,
            ExternalSubtitles = ExternalSubtitles,
            Expiration = Expiration,
            SourceKind = SourceKind,
            ReportingContext = new ReportingContext(
                ProviderId,
                ItemId,
                PlaySessionId,
                ProgressUri: null,
                MediaSourceId,
                Method.ToString()),
        };

    /// <summary>
    /// Stable identity for resume storage. Stream URLs carry a fresh
    /// <c>PlaySessionId</c> (and the api_key) on every resolve, so they can never
    /// be the key; a local DirectPlay file is keyed by its file URI.
    /// </summary>
    public string ProgressKey =>
        Uri.IsFile || string.IsNullOrWhiteSpace(ItemId)
            ? Uri.AbsoluteUri
            : $"{ProviderId ?? "server"}:{ItemId}:{MediaSourceId ?? ""}";
}
