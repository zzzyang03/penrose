namespace Penrose.Core.Sources;

/// <summary>One page of a folder listing. <see cref="TotalCount"/> is null when the server does not report it.</summary>
public sealed record LibraryPage(IReadOnlyList<LibraryItem> Items, int? TotalCount);

/// <summary>
/// Ordering of a library listing. <see cref="Field"/> uses the server's own sort
/// names (Emby: SortName, DateCreated, PremiereDate, ProductionYear,
/// CommunityRating, Runtime, PlayCount, DatePlayed, Random).
/// </summary>
public sealed record LibrarySort(string Field, bool Descending)
{
    public static readonly LibrarySort Default = new("SortName", false);

    /// <summary>The fields a library root can be ordered by, in menu order.</summary>
    public static readonly IReadOnlyList<string> Fields =
        ["SortName", "DateCreated", "PremiereDate", "ProductionYear", "CommunityRating", "Runtime", "PlayCount", "DatePlayed", "Random"];

    /// <summary>Newest / best first reads naturally for these; names and runtimes ascend.</summary>
    public static bool DefaultsToDescending(string field) =>
        field is "DateCreated" or "PremiereDate" or "ProductionYear" or "CommunityRating" or "PlayCount" or "DatePlayed";
}

/// <summary>One audio / video / subtitle stream of a playable item, for the details page.</summary>
public sealed record MediaStreamInfo(
    string Type,
    string? Codec,
    string? Profile,
    string? Language,
    string? Title,
    int? Width,
    int? Height,
    string? VideoRange,
    int? Channels,
    string? ChannelLayout,
    bool IsDefault,
    bool IsExternal,
    long? BitRate,
    double? FrameRate = null,
    /// <summary>The server's stream index (container order), used to pick tracks for playback.</summary>
    int Index = -1);

/// <summary>One version (file) of a playable item, with its streams.</summary>
public sealed record MediaSourceInfo(
    string Id,
    string Name,
    string? Container,
    long? Size,
    long? Bitrate,
    IReadOnlyList<MediaStreamInfo> Streams);

/// <summary>What the user picked on the item page before pressing play; nulls keep the server / player defaults.</summary>
public sealed record PlaybackSelection(string? MediaSourceId, int? AudioStreamIndex, int? SubtitleStreamIndex)
{
    public static readonly PlaybackSelection Default = new(null, null, null);
}

/// <summary>Cast / crew entry. <see cref="Role"/> is the character for actors; <see cref="Type"/> is Actor / Director / Writer / …</summary>
public sealed record PersonInfo(string Id, string Name, string? Role, string Type, string? ImageTag);

/// <summary>Link to the item on an external site (IMDb, TheMovieDb, …).</summary>
public sealed record ExternalLink(string Name, string Url);

/// <summary>Everything the details page shows for one item beyond its listing row.</summary>
public sealed record LibraryItemDetails(
    LibraryItem Item,
    string? Overview,
    string? Tagline,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Studios,
    string? OfficialRating,
    double? CommunityRating,
    DateTimeOffset? PremiereDate,
    string? Container,
    IReadOnlyList<MediaStreamInfo> Streams,
    /// <summary>Item whose Backdrop image to show (the series for episodes / seasons), with its tag.</summary>
    string? BackdropItemId,
    string? BackdropTag)
{
    public string? OriginalTitle { get; init; }

    /// <summary>Series: "Continuing" / "Ended".</summary>
    public string? Status { get; init; }

    public DateTimeOffset? EndDate { get; init; }

    public bool IsFavorite { get; init; }

    public bool Played { get; init; }

    public IReadOnlyList<PersonInfo> People { get; init; } = [];

    public IReadOnlyList<ExternalLink> ExternalUrls { get; init; } = [];

    /// <summary>All backdrop tags of <see cref="BackdropItemId"/>; index 0 is <see cref="BackdropTag"/>.</summary>
    public IReadOnlyList<string> BackdropTags { get; init; } = [];

    /// <summary>The file behind the first media source: its name, byte size and total bitrate.</summary>
    public string? SourceName { get; init; }

    public long? SourceSize { get; init; }

    public long? SourceBitrate { get; init; }

    /// <summary>Every version of the item; <see cref="Streams"/> belongs to the first.</summary>
    public IReadOnlyList<MediaSourceInfo> Sources { get; init; } = [];
}

public interface IContentProvider
{
    /// <summary>First page of <paramref name="parentId"/> (null = the top-level libraries).</summary>
    Task<IReadOnlyList<LibraryItem>> BrowseAsync(string? parentId, CancellationToken cancellationToken = default);

    /// <summary>Paged listing for large folders; pages are <see cref="PageSize"/> long.</summary>
    Task<LibraryPage> BrowsePageAsync(string? parentId, int startIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same, with the folder item itself so the provider can list a library root the
    /// way its own clients do (titles recursively, not physical folders), and the
    /// ordering for such root listings (ignored for seasons / episodes, which keep
    /// their natural order).
    /// </summary>
    Task<LibraryPage> BrowsePageAsync(string? parentId, int startIndex, LibraryItem? parent, LibrarySort? sort = null, CancellationToken cancellationToken = default);

    /// <summary>Full details of one item, or null when it no longer exists.</summary>
    Task<LibraryItemDetails?> GetDetailsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Wide backdrop art for the details page, or null when the item has none.</summary>
    Uri? BackdropUrl(LibraryItemDetails details, int maxWidth);

    /// <summary>The n-th backdrop (art strip), or null when there is no such one.</summary>
    Uri? BackdropUrl(LibraryItemDetails details, int index, int maxWidth);

    /// <summary>Portrait of a cast / crew member, or null when the server has none.</summary>
    Uri? PersonImageUrl(PersonInfo person, int maxHeight);

    /// <summary>Titles the server considers similar ("更多类似").</summary>
    Task<IReadOnlyList<LibraryItem>> SimilarAsync(string id, int limit, CancellationToken cancellationToken = default);

    /// <summary>The episodes a series page offers to play next (unwatched, in order).</summary>
    Task<IReadOnlyList<LibraryItem>> NextUpAsync(string seriesId, int limit, CancellationToken cancellationToken = default);

    Task SetPlayedAsync(string id, bool played, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(string id, bool favorite, CancellationToken cancellationToken = default);

    int PageSize { get; }

    Task<IReadOnlyList<LibraryItem>> SearchAsync(string query, CancellationToken cancellationToken = default);

    Task<LibraryItem?> GetMetadataAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>In-progress videos, most recent first ("continue watching").</summary>
    Task<IReadOnlyList<LibraryItem>> ContinueWatchingAsync(CancellationToken cancellationToken = default);

    /// <summary>Episode <paramref name="delta"/> steps from <paramref name="current"/> (crossing seasons), or null.</summary>
    Task<LibraryItem?> NeighbourEpisodeAsync(LibraryItem current, int delta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Poster / thumbnail the UI can load directly (the token rides in the query for
    /// the server's own origin), or null when neither the item nor its series has art.
    /// </summary>
    Uri? ImageUrl(LibraryItem item, int maxWidth, int maxHeight);
}
