using Penrose.Core.Playback;

namespace Penrose.Core.Sources;

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

    public PlaybackRequest ToPlaybackRequest(Guid requestId) =>
        new()
        {
            RequestId = requestId,
            Uri = Uri,
            Headers = Headers,
            ExternalSubtitles = ExternalSubtitles,
            Expiration = Expiration,
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
