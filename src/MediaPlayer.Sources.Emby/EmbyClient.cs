using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediaPlayer.Core.Sources;

namespace MediaPlayer.Sources.Emby;

public sealed class EmbySession
{
    public required string AccessToken { get; init; }

    public required string UserId { get; init; }

    public required string UserName { get; init; }

    public string? ServerVersion { get; init; }
}

/// <summary>Why an Emby HTTP call failed, for a message the user can act on.</summary>
public enum EmbyFailureKind
{
    /// <summary>Non-2xx with no better explanation.</summary>
    Other,

    /// <summary>401: the server rejected the credentials or the token.</summary>
    Unauthorized,

    /// <summary>
    /// A Cloudflare "Sorry, you have been blocked" page: the server's firewall
    /// refuses this network's egress IP (commonly a geo rule on proxied servers).
    /// </summary>
    CloudflareBlocked,

    /// <summary>An HTML page instead of the API: wrong URL, reverse-proxy portal, captive login.</summary>
    HtmlPage,
}

/// <summary><see cref="HttpRequestException"/> carrying the classified <see cref="Kind"/>.</summary>
public sealed class EmbyHttpException : HttpRequestException
{
    public EmbyHttpException(string message, HttpStatusCode statusCode, EmbyFailureKind kind)
        : base(message, inner: null, statusCode)
    {
        Kind = kind;
    }

    public EmbyFailureKind Kind { get; }
}

public sealed class EmbyClient : IDisposable
{
    public const string DefaultDeviceId = "mediaplayer";

    /// <summary>Items per page for library listings; the server default is 100.</summary>
    public const int PageSize = 200;

    private readonly HttpClient _http;
    private readonly string _clientName;
    private readonly string _device;
    private readonly string _deviceId;
    private readonly string _version;
    private string? _token;

    public EmbyClient(Uri baseAddress, string device = "Windows", string deviceId = DefaultDeviceId, string version = "0.0.1")
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _clientName = DeviceProfileBuilder.ClientName;
        _device = device;
        _deviceId = deviceId;
        _version = version;
        // Large libraries take a while to page; interactive callers pass their own
        // shorter CancellationToken.
        _http = new HttpClient { BaseAddress = NormalizeBase(baseAddress), Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // .NET sends no User-Agent by default; WAF bot rules in front of public
        // servers reject UA-less requests outright.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent(version));
    }

    public static string UserAgent(string version) =>
        DeviceProfileBuilder.ClientName + "/" + (string.IsNullOrWhiteSpace(version) ? "0" : version.Trim()) + " (Windows)";

    /// <summary>
    /// Classifies a non-2xx response. Cloudflare's block page is recognised by its
    /// title / headline so the user learns it is the firewall, not the password.
    /// </summary>
    public static EmbyFailureKind Classify(HttpStatusCode status, string? mediaType, string? body)
    {
        body ??= "";
        bool html = (mediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) ?? false)
            || body.AsSpan().TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || body.AsSpan().TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        if (html && body.Contains("cloudflare", StringComparison.OrdinalIgnoreCase)
            && (body.Contains("you have been blocked", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Attention Required", StringComparison.OrdinalIgnoreCase)
                || status == HttpStatusCode.Forbidden))
        {
            return EmbyFailureKind.CloudflareBlocked;
        }

        if (status == HttpStatusCode.Unauthorized)
        {
            return EmbyFailureKind.Unauthorized;
        }

        return html ? EmbyFailureKind.HtmlPage : EmbyFailureKind.Other;
    }

    public Uri BaseAddress => _http.BaseAddress!;

    public string? AccessToken => _token;

    public void SetAccessToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _token = token;
    }

    /// <summary>
    /// A reverse-proxy sub-path (<c>https://host/media</c>) survives only when the
    /// base ends with '/'; otherwise <c>new Uri(base, "emby/...")</c> drops it.
    /// </summary>
    public static Uri NormalizeBase(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        string text = baseAddress.AbsoluteUri;
        return text.EndsWith('/') ? baseAddress : new Uri(text + "/");
    }

    public static async Task<JsonElement> GetPublicInfoAsync(Uri baseAddress, CancellationToken cancellationToken = default)
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent("0"));
        using HttpResponseMessage response = await http.GetAsync(new Uri(NormalizeBase(baseAddress), "emby/System/Info/Public"), cancellationToken)
            .ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw Failure(HttpMethod.Get, "emby/System/Info/Public", response, text);
        }

        using JsonDocument doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    public async Task<EmbySession> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        string body = JsonSerializer.Serialize(new { Username = username, Pw = password });
        using JsonDocument doc = await SendAsync(
            HttpMethod.Post,
            "emby/Users/AuthenticateByName",
            body,
            includeToken: false,
            cancellationToken).ConfigureAwait(false);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Emby login returned no JSON object.");
        }

        string token = ReadString(root, "AccessToken")
            ?? throw new InvalidOperationException("Emby login returned no AccessToken.");
        if (!root.TryGetProperty("User", out JsonElement user) || user.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Emby login returned no User.");
        }

        string userId = ReadString(user, "Id") ?? throw new InvalidOperationException("Emby login returned no User.Id.");
        string userName = ReadString(user, "Name") ?? username;
        _token = token;
        string? version = null;
        try
        {
            using JsonDocument info = await SendAsync(HttpMethod.Get, "emby/System/Info", body: null, includeToken: true, cancellationToken)
                .ConfigureAwait(false);
            version = ReadString(info.RootElement, "Version");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            // Public version is enough.
        }

        return new EmbySession
        {
            AccessToken = token,
            UserId = userId,
            UserName = userName,
            ServerVersion = version,
        };
    }

    /// <summary>Listing fields: cheap metadata only. MediaSources / MediaStreams come from PlaybackInfo.</summary>
    private const string ListingFields = "Path,Container,Overview,ProductionYear,SeriesName,ParentIndexNumber,IndexNumber,CollectionType";

    public Task<JsonDocument> GetViewsAsync(string userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "emby/Users/" + Uri.EscapeDataString(userId) + "/Views", null, true, cancellationToken);

    /// <summary>
    /// Children of a folder. Without <paramref name="recursiveTypes"/>: the direct
    /// children (series → seasons → episodes, box set → movies), folders first, then
    /// by season / episode number, then by name. With it: everything of those types
    /// anywhere below the folder, by name — how a library root is shown (its direct
    /// children are only the physical folders behind it).
    /// </summary>
    public Task<JsonDocument> GetChildrenAsync(
        string userId,
        string parentId,
        int startIndex = 0,
        string? recursiveTypes = null,
        LibrarySort? sort = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, ChildrenPath(userId, parentId, startIndex, recursiveTypes, sort), null, true, cancellationToken);

    /// <summary>
    /// Relative URL of a children page. Folder children keep the natural order
    /// (folders first, then season / episode numbers); recursive library listings
    /// take the user's sort, with SortName as the tie-breaker the way Emby's own
    /// web client sends it.
    /// </summary>
    public static string ChildrenPath(string userId, string parentId, int startIndex, string? recursiveTypes, LibrarySort? sort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        StringBuilder path = new();
        path.Append("emby/Users/").Append(Uri.EscapeDataString(userId))
            .Append("/Items?ParentId=").Append(Uri.EscapeDataString(parentId));
        if (string.IsNullOrWhiteSpace(recursiveTypes))
        {
            path.Append("&SortBy=IsFolder,ParentIndexNumber,IndexNumber,SortName&SortOrder=Ascending");
        }
        else
        {
            LibrarySort order = sort ?? LibrarySort.Default;
            string field = LibrarySort.Fields.Contains(order.Field, StringComparer.Ordinal) ? order.Field : "SortName";
            path.Append("&Recursive=true&IncludeItemTypes=").Append(Uri.EscapeDataString(recursiveTypes))
                .Append("&SortBy=").Append(field == "SortName" ? "SortName" : field + ",SortName")
                .Append("&SortOrder=").Append(order.Descending ? "Descending" : "Ascending");
        }

        path.Append("&Fields=").Append(ListingFields)
            .Append("&Limit=").Append(PageSize)
            .Append("&StartIndex=").Append(Math.Max(0, startIndex));
        return path.ToString();
    }

    /// <summary>In-progress videos for "continue watching", most recently played first.</summary>
    public Task<JsonDocument> GetResumeItemsAsync(string userId, int limit = 24, CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            "emby/Users/" + Uri.EscapeDataString(userId)
                + "/Items/Resume?Recursive=true&MediaTypes=Video&Limit=" + Math.Clamp(limit, 1, 100)
                + "&Fields=" + ListingFields,
            null,
            true,
            cancellationToken);

    /// <summary>Tail bytes warmed: Matroska Cues for a feature-length file fit comfortably.</summary>
    public const long WarmTailBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Reads the first bytes of a media URL — and, for <paramref name="tail"/>,
    /// its last <see cref="WarmTailBytes"/> — and discards them. On servers whose
    /// files live on slow storage the first request touching a region takes many
    /// seconds while later ones are quick; Matroska keeps its Cues at the end and
    /// mpv reads them while opening (measured: head, cues, back to the start —
    /// three cold regions, 3–7 s each), so warming both ends ahead of the real
    /// open removes most of the wait. Never throws; the outcome only matters for timing.
    /// </summary>
    public async Task<bool> WarmAsync(Uri mediaUrl, IReadOnlyDictionary<string, string> headers, bool tail = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaUrl);
        ArgumentNullException.ThrowIfNull(headers);
        // Sequential on purpose: the server handles one region of a file at a time.
        // Shaped like mpv's own requests (open-ended ranges from an offset) so the
        // server's read-ahead for that offset is what mpv's later request hits.
        (bool head, long? size) = await TouchAsync(mediaUrl, headers, from: 0, limit: 64 * 1024, cancellationToken).ConfigureAwait(false);
        bool end = true;
        if (tail && size is > WarmTailBytes)
        {
            (end, _) = await TouchAsync(mediaUrl, headers, from: size.Value - WarmTailBytes, limit: WarmTailBytes, cancellationToken).ConfigureAwait(false);
        }

        return head && end;
    }

    /// <summary>GET with <c>Range: bytes={from}-</c>, reads up to <paramref name="limit"/> bytes, reports the total size.</summary>
    private async Task<(bool Ok, long? Size)> TouchAsync(Uri mediaUrl, IReadOnlyDictionary<string, string> headers, long from, long limit, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, mediaUrl);
            foreach ((string name, string value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            request.Headers.Range = new RangeHeaderValue(from, null);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }

            long? size = response.Content.Headers.ContentRange?.Length;
            // The region only counts as warm once the server has actually produced it.
            using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            byte[] sink = new byte[64 * 1024];
            long remaining = limit;
            while (remaining > 0)
            {
                int n = await body.ReadAsync(sink.AsMemory(0, (int)Math.Min(sink.Length, remaining)), timeout.Token).ConfigureAwait(false);
                if (n <= 0)
                {
                    break;
                }

                remaining -= n;
            }

            return (true, size);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return (false, null);
        }
    }

    /// <summary>Item types a library root lists recursively, by its collection type; null = plain children.</summary>
    public static string? RecursiveTypesFor(string? collectionType) => collectionType?.ToLowerInvariant() switch
    {
        "movies" => "Movie",
        "tvshows" => "Series",
        "music" => "MusicAlbum",
        "musicvideos" => "MusicVideo",
        "boxsets" => "BoxSet",
        "playlists" => "Playlist",
        "homevideos" or "photos" => "Video,Photo",
        _ => null,
    };

    /// <summary>
    /// Flat recursive listing (movies + episodes) — used for search; a whole
    /// library this way is paged and slow on large servers.
    /// </summary>
    public Task<JsonDocument> GetItemsAsync(
        string userId,
        string? parentId = null,
        CancellationToken cancellationToken = default) =>
        GetItemsAsync(userId, parentId, searchTerm: null, startIndex: 0, cancellationToken);

    public Task<JsonDocument> GetItemsAsync(
        string userId,
        string? parentId,
        string? searchTerm,
        int startIndex,
        CancellationToken cancellationToken = default)
    {
        StringBuilder path = new();
        path.Append("emby/Users/").Append(Uri.EscapeDataString(userId))
            .Append("/Items?Recursive=true&IncludeItemTypes=Movie,Series,Episode")
            .Append("&SortBy=SortName&SortOrder=Ascending")
            .Append("&Fields=").Append(ListingFields)
            .Append("&Limit=").Append(PageSize)
            .Append("&StartIndex=").Append(Math.Max(0, startIndex));
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            path.Append("&ParentId=").Append(Uri.EscapeDataString(parentId));
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            path.Append("&SearchTerm=").Append(Uri.EscapeDataString(searchTerm));
        }

        return SendAsync(HttpMethod.Get, path.ToString(), null, true, cancellationToken);
    }

    /// <summary>Fields the details page needs on top of the listing ones.</summary>
    private const string DetailFields =
        "Overview,Genres,Taglines,Studios,OfficialRating,CommunityRating,PremiereDate,EndDate,ProductionYear,RunTimeTicks,"
        + "Container,MediaSources,MediaStreams,Path,ParentId,SeasonId,SeriesId,SeriesName,IndexNumber,ParentIndexNumber,"
        + "BackdropImageTags,ParentBackdropImageTags,ParentBackdropItemId,People,ExternalUrls,OriginalTitle,Status";

    public Task<JsonDocument> GetItemAsync(string userId, string itemId, CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            "emby/Users/" + Uri.EscapeDataString(userId) + "/Items/" + Uri.EscapeDataString(itemId) + "?Fields=" + DetailFields,
            null,
            true,
            cancellationToken);

    /// <summary><c>emby/Items/{id}/Images/Backdrop/0</c> scaled by the server, token in the query.</summary>
    public Uri BackdropUrl(string itemId, string? tag, int maxWidth) => BackdropUrl(itemId, 0, tag, maxWidth);

    public Task<JsonDocument> GetPlaybackInfoAsync(
        string userId,
        string itemId,
        DeviceProfile profile,
        CancellationToken cancellationToken = default)
    {
        string path = "emby/Items/" + Uri.EscapeDataString(itemId) + "/PlaybackInfo?UserId=" + Uri.EscapeDataString(userId);
        string body = EmbyDeviceProfileJson.PlaybackInfoBody(profile);
        return SendAsync(HttpMethod.Post, path, body, true, cancellationToken);
    }

    public Task ReportPlayingAsync(object payload, CancellationToken cancellationToken = default) =>
        SendDiscardAsync(HttpMethod.Post, "emby/Sessions/Playing", JsonSerializer.Serialize(payload), cancellationToken);

    public Task ReportProgressAsync(object payload, CancellationToken cancellationToken = default) =>
        SendDiscardAsync(HttpMethod.Post, "emby/Sessions/Playing/Progress", JsonSerializer.Serialize(payload), cancellationToken);

    public Task ReportStoppedAsync(object payload, CancellationToken cancellationToken = default) =>
        SendDiscardAsync(HttpMethod.Post, "emby/Sessions/Playing/Stopped", JsonSerializer.Serialize(payload), cancellationToken);

    public Task MarkPlayedAsync(string userId, string itemId, CancellationToken cancellationToken = default) =>
        SetPlayedAsync(userId, itemId, played: true, cancellationToken);

    /// <summary>POST / DELETE <c>Users/{id}/PlayedItems/{itemId}</c>.</summary>
    public Task SetPlayedAsync(string userId, string itemId, bool played, CancellationToken cancellationToken = default) =>
        SendDiscardAsync(
            played ? HttpMethod.Post : HttpMethod.Delete,
            "emby/Users/" + Uri.EscapeDataString(userId) + "/PlayedItems/" + Uri.EscapeDataString(itemId),
            body: null,
            cancellationToken);

    /// <summary>POST / DELETE <c>Users/{id}/FavoriteItems/{itemId}</c>.</summary>
    public Task SetFavoriteAsync(string userId, string itemId, bool favorite, CancellationToken cancellationToken = default) =>
        SendDiscardAsync(
            favorite ? HttpMethod.Post : HttpMethod.Delete,
            "emby/Users/" + Uri.EscapeDataString(userId) + "/FavoriteItems/" + Uri.EscapeDataString(itemId),
            body: null,
            cancellationToken);

    /// <summary><c>emby/Items/{id}/Similar</c>: what the server's own "更多类似" strip shows.</summary>
    public Task<JsonDocument> GetSimilarAsync(string userId, string itemId, int limit, CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            "emby/Items/" + Uri.EscapeDataString(itemId) + "/Similar?UserId=" + Uri.EscapeDataString(userId)
                + "&Limit=" + Math.Clamp(limit, 1, 50) + "&Fields=" + ListingFields,
            null,
            true,
            cancellationToken);

    /// <summary><c>emby/Shows/NextUp</c> for one series: the next unwatched episode(s).</summary>
    public Task<JsonDocument> GetNextUpAsync(string userId, string seriesId, int limit, CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            "emby/Shows/NextUp?UserId=" + Uri.EscapeDataString(userId) + "&SeriesId=" + Uri.EscapeDataString(seriesId)
                + "&Limit=" + Math.Clamp(limit, 1, 20) + "&Fields=" + ListingFields,
            null,
            true,
            cancellationToken);

    /// <summary><c>emby/Items/{id}/Images/Backdrop/{index}</c> scaled by the server, token in the query.</summary>
    public Uri BackdropUrl(string itemId, int index, string? tag, int maxWidth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        StringBuilder path = new();
        path.Append("emby/Items/").Append(Uri.EscapeDataString(itemId)).Append("/Images/Backdrop/")
            .Append(Math.Max(0, index)).Append("?quality=85&maxWidth=").Append(Math.Max(1, maxWidth));
        if (!string.IsNullOrEmpty(tag))
        {
            path.Append("&tag=").Append(Uri.EscapeDataString(tag));
        }

        if (!string.IsNullOrEmpty(_token))
        {
            path.Append("&api_key=").Append(Uri.EscapeDataString(_token));
        }

        return new Uri(BaseAddress, path.ToString());
    }

    /// <summary>Primary image of a person item (cast strip), bounded by height.</summary>
    public Uri PersonImageUrl(string personId, string? tag, int maxHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personId);
        StringBuilder path = new();
        path.Append("emby/Items/").Append(Uri.EscapeDataString(personId)).Append("/Images/Primary?quality=90")
            .Append("&maxHeight=").Append(Math.Max(1, maxHeight));
        if (!string.IsNullOrEmpty(tag))
        {
            path.Append("&tag=").Append(Uri.EscapeDataString(tag));
        }

        if (!string.IsNullOrEmpty(_token))
        {
            path.Append("&api_key=").Append(Uri.EscapeDataString(_token));
        }

        return new Uri(BaseAddress, path.ToString());
    }

    /// <summary>
    /// <c>emby/Items/{id}/Images/Primary</c> scaled by the server. Same-origin, so
    /// the token may ride in the query (the image control cannot send headers).
    /// </summary>
    public Uri ImageUrl(string itemId, string? tag, int maxWidth, int maxHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        StringBuilder path = new();
        path.Append("emby/Items/").Append(Uri.EscapeDataString(itemId)).Append("/Images/Primary?quality=90")
            .Append("&maxWidth=").Append(Math.Max(1, maxWidth))
            .Append("&maxHeight=").Append(Math.Max(1, maxHeight));
        if (!string.IsNullOrEmpty(tag))
        {
            path.Append("&tag=").Append(Uri.EscapeDataString(tag));
        }

        if (!string.IsNullOrEmpty(_token))
        {
            path.Append("&api_key=").Append(Uri.EscapeDataString(_token));
        }

        return new Uri(BaseAddress, path.ToString());
    }

    public void Dispose() => _http.Dispose();

    public string AuthorizationHeader()
    {
        string header =
            $"MediaBrowser Client=\"{_clientName}\", Device=\"{_device}\", DeviceId=\"{_deviceId}\", Version=\"{_version}\"";
        if (!string.IsNullOrEmpty(_token))
        {
            header += $", Token=\"{_token}\"";
        }

        return header;
    }

    private async Task SendDiscardAsync(HttpMethod method, string relative, string? body, CancellationToken cancellationToken)
    {
        using JsonDocument doc = await SendAsync(method, relative, body, includeToken: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relative,
        string? body,
        bool includeToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, relative);
        request.Headers.TryAddWithoutValidation("X-Emby-Authorization", AuthorizationHeader());
        if (includeToken && !string.IsNullOrEmpty(_token))
        {
            request.Headers.TryAddWithoutValidation("X-Emby-Token", _token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post)
        {
            // Emby rejects a bodyless POST with 411/415 on some proxies.
            request.Content = new StringContent("", Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw Failure(method, relative, response, text);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return JsonDocument.Parse("null");
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            // A 200 with HTML is a portal / wrong sub-path, not a parser bug.
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            throw new EmbyHttpException(
                $"Emby {method} {RedactPath(relative)} returned non-JSON content ({mediaType ?? "unknown"}).",
                response.StatusCode,
                Classify(response.StatusCode, mediaType, text) is EmbyFailureKind.HtmlPage or EmbyFailureKind.CloudflareBlocked ? EmbyFailureKind.HtmlPage : EmbyFailureKind.Other);
        }
    }

    /// <summary>
    /// Keeps the status code on the exception so callers (retry, re-auth) can tell
    /// 401/404 from a transport failure. The path never carries a token, and the
    /// body is classified rather than quoted.
    /// </summary>
    private static EmbyHttpException Failure(HttpMethod method, string relative, HttpResponseMessage response, string body)
    {
        EmbyFailureKind kind = Classify(response.StatusCode, response.Content.Headers.ContentType?.MediaType, body);
        string detail = kind switch
        {
            EmbyFailureKind.CloudflareBlocked => "Blocked by the server's Cloudflare firewall (this network's IP is refused).",
            EmbyFailureKind.Unauthorized => "Credentials or token rejected.",
            EmbyFailureKind.HtmlPage => "Server returned an HTML page instead of the Emby API (check the URL / reverse proxy).",
            _ => $"Body length {body.Length}.",
        };
        return new EmbyHttpException(
            $"Emby {method} {RedactPath(relative)} -> {(int)response.StatusCode}. {detail}",
            response.StatusCode,
            kind);
    }

    private static string RedactPath(string relative)
    {
        int query = relative.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? relative : relative[..query];
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
