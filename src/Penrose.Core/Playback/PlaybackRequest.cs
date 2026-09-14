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

        if (StartPosition is { } start)
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
    /// File-local tuning for HTTP(S) media. Measured against a public Emby: every
    /// cold region of a file costs 3–7 s on the server and every new connection
    /// ~1.4 s, so the open phase must not read more than it needs and must survive
    /// the odd dropped connection instead of failing the load. (This libmpv opens
    /// https through its curl stream, so <c>stream-lavf-o</c> only covers builds
    /// and URLs that fall back to FFmpeg's http.)
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NetworkOpenOptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // FFmpeg http: reconnect on drops / transient network errors, with backoff.
        ["stream-lavf-o"] = "reconnect=1,reconnect_on_network_error=1,reconnect_delay_max=5",
        // Stream info from the first 2 MiB / 2 s instead of FFmpeg's 5 MiB / 5 s.
        ["demuxer-lavf-probesize"] = "2000000",
        ["demuxer-lavf-analyzeduration"] = "2",
    };

    public static bool IsHttp(Uri uri) =>
        uri is { IsAbsoluteUri: true } && (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps);

    private static string EscapeListItem(string item) =>
        item.Replace(",", "\\,", StringComparison.Ordinal);
}
