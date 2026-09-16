namespace Penrose.Core.Playback;

/// <summary>
/// One load operation. Network credentials belong here and must be applied as
/// file-local loadfile options, never as global mpv properties.
/// </summary>
public sealed record PlaybackRequest
{
    public required Guid RequestId { get; init; }
    public required Uri Uri { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();
    public string? UserAgent { get; init; }
    public string? Cookies { get; init; }
    public IReadOnlyList<ExternalSubtitle> ExternalSubtitles { get; init; } = [];
    public TimeSpan? StartPosition { get; init; }
    /// <summary>
    /// 1-based DVD/BD title index. Applied as mpv <c>disc-title</c> after
    /// FILE_LOADED. Must not be written as loadfile <c>title</c> (window title).
    /// </summary>
    public int? DiscTitle { get; init; }
    /// <summary>Initial audio track as mpv <c>aid</c> (1-based among audio tracks); null keeps mpv's choice.</summary>
    public int? AudioTrack { get; init; }
    /// <summary>Initial subtitle track as mpv <c>sid</c> (1-based among subtitle tracks, 0 = none); null keeps mpv's choice.</summary>
    public int? SubtitleTrack { get; init; }
    public DateTimeOffset? Expiration { get; init; }
    public ReportingContext? ReportingContext { get; init; }
    public HdrPolicy HdrPolicy { get; init; } = HdrPolicy.AutoTarget;
    public AudioPolicy AudioPolicy { get; init; } = AudioPolicy.SystemCompatible;

    /// <summary>
    /// What kind of medium this request plays. Defaults to
    /// <see cref="Core.Sources.MediaSourceKind.Unknown"/>; the local factory and
    /// the Emby/Jellyfin parsers set it. The info overlay labels the playback
    /// ("本地播放" / "strm 中继" / "服务器转码" / …) from this field.
    /// </summary>
    public Core.Sources.MediaSourceKind SourceKind { get; init; } =
        Core.Sources.MediaSourceKind.Unknown;

    /// <summary>
    /// File-local options for loadfile. Must not be written to global
    /// http-header-fields / user-agent / cookies.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToFileLocalOptions()
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);

        List<string> headerFields = Headers
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToList();
        if (!string.IsNullOrWhiteSpace(Cookies) &&
            headerFields.TrueForAll(field => !field.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)))
        {
            headerFields.Add("Cookie: " + Cookies);
        }

        if (headerFields.Count > 0)
        {
            // http-header-fields is an mpv string list: items are separated by ','
            // and a literal comma inside an item is written as '\,'.
            options["http-header-fields"] = string.Join(",", headerFields.Select(EscapeListItem));
        }

        if (!string.IsNullOrWhiteSpace(UserAgent))
        {
            options["user-agent"] = UserAgent;
        }

        if (StartPosition is { } start && !IsHttp(Uri))
        {
            options["start"] = start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (AudioTrack is { } aid && aid > 0)
        {
            options["aid"] = aid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (SubtitleTrack is { } sid)
        {
            options["sid"] = sid <= 0 ? "no" : sid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (IsHttp(Uri))
        {
            foreach ((string key, string value) in NetworkOpenOptions)
            {
                options[key] = value;
            }
        }

        return options;
    }

    /// <summary>
    /// File-local tuning for HTTP(S) media. Reconnect survives a dropped cloud
    /// 302. The lavf probe must be large enough to see audio and Dolby Vision in a
    /// 4K remux / WEB-DL after OpenList redirects; 2 MiB / 2 s (below FFmpeg's
    /// 5 MiB / 5 s default) missed those streams and started the file silent.
    /// These caps are maxima: a complete header still finishes as soon as lavf
    /// has every stream. (This libmpv opens https through its curl stream, so
    /// <c>stream-lavf-o</c> only covers builds and URLs that fall back to FFmpeg's
    /// http.)
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NetworkOpenOptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // FFmpeg http: reconnect on drops / transient network errors, with backoff.
        ["stream-lavf-o"] = "reconnect=1,reconnect_on_network_error=1,reconnect_delay_max=5",
        ["demuxer-lavf-probesize"] = "10000000",
        ["demuxer-lavf-analyzeduration"] = "6",
        // Cloud 302 URLs usually support Range; without this mpv may treat the
        // open as unseekable and ignore the FILE_LOADED resume seek.
        ["force-seekable"] = "yes",
    };

    public static bool IsHttp(Uri uri) =>
        uri is { IsAbsoluteUri: true } && (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps);

    /// <summary>
    /// HTTP resume opens from byte 0 and seeks once FILE_LOADED arrives instead
    /// of passing loadfile <c>start=</c>. It was added for resumes that froze
    /// until the user dragged the slider; that freeze was later traced to mpv
    /// stalling after a refused spdif output fell back to PCM (handled in
    /// <c>MpvPlaybackEngine.LeaveRefusedBitstream</c>), not to <c>start=</c>.
    /// Kept because it is verified on HTTP sources; local files still use
    /// <c>start=</c>.
    /// </summary>
    public bool SeekAfterOpen =>
        StartPosition is { Ticks: > 0 } && IsHttp(Uri);

    private static string EscapeListItem(string item) =>
        item.Replace(",", "\\,", StringComparison.Ordinal);
}
