using System.Text.Json;
using MediaPlayer.Core.Sources;

namespace MediaPlayer.Sources.Emby;

public sealed class EmbyContentProvider : IContentProvider
{
    private readonly EmbyClient _client;
    private readonly string _userId;

    public EmbyContentProvider(EmbyClient client, string userId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        _userId = userId;
    }

    /// <summary>
    /// Root (<paramref name="parentId"/> null) lists the user's library views;
    /// anything else lists the folder's direct children.
    /// </summary>
    public Task<IReadOnlyList<LibraryItem>> BrowseAsync(
        string? parentId,
        CancellationToken cancellationToken = default) =>
        BrowseAsync(parentId, startIndex: 0, cancellationToken);

    public async Task<IReadOnlyList<LibraryItem>> BrowseAsync(
        string? parentId,
        int startIndex,
        CancellationToken cancellationToken = default) =>
        (await BrowsePageAsync(parentId, startIndex, cancellationToken).ConfigureAwait(false)).Items;

    public int PageSize => EmbyClient.PageSize;

    public Task<LibraryPage> BrowsePageAsync(
        string? parentId,
        int startIndex,
        CancellationToken cancellationToken = default) =>
        BrowsePageAsync(parentId, startIndex, parent: null, cancellationToken);

    /// <param name="parent">
    /// The folder being opened, when known: a library root (CollectionFolder /
    /// UserView) lists its titles recursively by collection type instead of the
    /// physical folders Emby returns as direct children.
    /// </param>
    public Task<LibraryPage> BrowsePageAsync(
        string? parentId,
        int startIndex,
        LibraryItem? parent,
        CancellationToken cancellationToken) =>
        BrowsePageAsync(parentId, startIndex, parent, sort: null, cancellationToken);

    public async Task<LibraryPage> BrowsePageAsync(
        string? parentId,
        int startIndex,
        LibraryItem? parent,
        LibrarySort? sort = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentId))
        {
            using JsonDocument views = await _client.GetViewsAsync(_userId, cancellationToken).ConfigureAwait(false);
            List<LibraryItem> list = EmbyItemsParser.Parse(views.RootElement.GetRawText())
                .Select(v => v with { IsFolder = true })
                .ToList();
            return new LibraryPage(list, list.Count);
        }

        string? recursiveTypes = parent?.Kind is "CollectionFolder" or "UserView"
            ? EmbyClient.RecursiveTypesFor(parent.CollectionType)
            : null;
        using JsonDocument document = await _client
            .GetChildrenAsync(_userId, parentId, startIndex, recursiveTypes, sort, cancellationToken)
            .ConfigureAwait(false);
        string json = document.RootElement.GetRawText();
        return new LibraryPage(EmbyItemsParser.Parse(json), EmbyItemsParser.TotalCount(json));
    }

    public async Task<LibraryItemDetails?> GetDetailsAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            using JsonDocument document = await _client.GetItemAsync(_userId, id, cancellationToken).ConfigureAwait(false);
            return EmbyItemsParser.ParseDetails(document.RootElement.GetRawText());
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Uri? BackdropUrl(LibraryItemDetails details, int maxWidth) => BackdropUrl(details, 0, maxWidth);

    public Uri? BackdropUrl(LibraryItemDetails details, int index, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.BackdropItemId is not { Length: > 0 } id || index < 0 || index >= Math.Max(1, details.BackdropTags.Count))
        {
            return null;
        }

        string? tag = index < details.BackdropTags.Count ? details.BackdropTags[index] : details.BackdropTag;
        return _client.BackdropUrl(id, index, tag, maxWidth);
    }

    public Uri? PersonImageUrl(PersonInfo person, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(person);
        return string.IsNullOrEmpty(person.ImageTag) ? null : _client.PersonImageUrl(person.Id, person.ImageTag, maxHeight);
    }

    public async Task<IReadOnlyList<LibraryItem>> SimilarAsync(string id, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using JsonDocument document = await _client.GetSimilarAsync(_userId, id, limit, cancellationToken).ConfigureAwait(false);
        return EmbyItemsParser.Parse(document.RootElement.GetRawText());
    }

    public async Task<IReadOnlyList<LibraryItem>> NextUpAsync(string seriesId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesId);
        using JsonDocument document = await _client.GetNextUpAsync(_userId, seriesId, limit, cancellationToken).ConfigureAwait(false);
        return EmbyItemsParser.Parse(document.RootElement.GetRawText());
    }

    public Task SetPlayedAsync(string id, bool played, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _client.SetPlayedAsync(_userId, id, played, cancellationToken);
    }

    public Task SetFavoriteAsync(string id, bool favorite, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _client.SetFavoriteAsync(_userId, id, favorite, cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryItem>> ContinueWatchingAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await _client.GetResumeItemsAsync(_userId, 24, cancellationToken).ConfigureAwait(false);
        return EmbyItemsParser.Parse(document.RootElement.GetRawText());
    }

    /// <summary>
    /// The episode <paramref name="delta"/> steps away in the same season, crossing
    /// into the neighbouring season when the current one runs out. Null for
    /// non-episodes or at either end of the series.
    /// </summary>
    public async Task<LibraryItem?> NeighbourEpisodeAsync(LibraryItem current, int delta, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Kind != "Episode" || delta == 0)
        {
            return null;
        }

        string? seasonId = current.SeasonId ?? current.ParentId;
        if (string.IsNullOrEmpty(seasonId))
        {
            return null;
        }

        IReadOnlyList<LibraryItem> episodes = await EpisodesOfAsync(seasonId, cancellationToken).ConfigureAwait(false);
        int index = IndexOf(episodes, current);
        if (index >= 0 && index + delta >= 0 && index + delta < episodes.Count)
        {
            return episodes[index + delta];
        }

        // Off the end of this season: step to the neighbouring season of the series.
        if (string.IsNullOrEmpty(current.SeriesId))
        {
            return null;
        }

        IReadOnlyList<LibraryItem> seasons = (await BrowseAsync(current.SeriesId, cancellationToken).ConfigureAwait(false))
            .Where(s => s.Kind == "Season")
            .ToList();
        int seasonIndex = seasons.ToList().FindIndex(s => s.Id == seasonId);
        int target = seasonIndex + Math.Sign(delta);
        if (seasonIndex < 0 || target < 0 || target >= seasons.Count)
        {
            return null;
        }

        IReadOnlyList<LibraryItem> next = await EpisodesOfAsync(seasons[target].Id, cancellationToken).ConfigureAwait(false);
        return next.Count == 0 ? null : delta > 0 ? next[0] : next[^1];
    }

    private async Task<IReadOnlyList<LibraryItem>> EpisodesOfAsync(string seasonId, CancellationToken cancellationToken)
    {
        List<LibraryItem> all = [];
        for (int start = 0; ; start += PageSize)
        {
            LibraryPage page = await BrowsePageAsync(seasonId, start, parent: null, cancellationToken).ConfigureAwait(false);
            all.AddRange(page.Items.Where(i => !i.IsFolder));
            if (page.Items.Count < PageSize || (page.TotalCount is { } total && all.Count >= total))
            {
                break;
            }
        }

        return all;
    }

    private static int IndexOf(IReadOnlyList<LibraryItem> episodes, LibraryItem current)
    {
        for (int i = 0; i < episodes.Count; i++)
        {
            if (episodes[i].Id == current.Id)
            {
                return i;
            }
        }

        // Same episode number from another listing (e.g. the item came from Resume).
        if (current.IndexNumber is { } number)
        {
            for (int i = 0; i < episodes.Count; i++)
            {
                if (episodes[i].IndexNumber == number && episodes[i].ParentIndexNumber == current.ParentIndexNumber)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// The item's own Primary image, else the series poster for episodes / seasons.
    /// The tag makes the URL cache-stable; the token goes in the query because the
    /// image control cannot send headers, and the URL never leaves the server's origin.
    /// </summary>
    public Uri? ImageUrl(LibraryItem item, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.PrimaryImageTag is not null)
        {
            return _client.ImageUrl(item.Id, item.PrimaryImageTag, maxWidth, maxHeight);
        }

        if (item.SeriesId is not null && item.SeriesPrimaryImageTag is not null)
        {
            return _client.ImageUrl(item.SeriesId, item.SeriesPrimaryImageTag, maxWidth, maxHeight);
        }

        return null;
    }

    /// <summary>Server-side search across movies, series and episodes.</summary>
    public async Task<IReadOnlyList<LibraryItem>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        using JsonDocument document = await _client
            .GetItemsAsync(_userId, parentId: null, searchTerm: query.Trim(), startIndex: 0, cancellationToken)
            .ConfigureAwait(false);
        return EmbyItemsParser.Parse(document.RootElement.GetRawText());
    }

    public async Task<LibraryItem?> GetMetadataAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            using JsonDocument document = await _client.GetItemAsync(_userId, id, cancellationToken).ConfigureAwait(false);
            // The single-item endpoint returns the object itself; the parser also
            // accepts an un-wrapped object when given as a one-element array.
            return EmbyItemsParser.Parse("[" + document.RootElement.GetRawText() + "]").FirstOrDefault();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
