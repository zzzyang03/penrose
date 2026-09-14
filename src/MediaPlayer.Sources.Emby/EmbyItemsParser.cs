using System.Text.Json;
using MediaPlayer.Core.Sources;

namespace MediaPlayer.Sources.Emby;

/// <summary>Parses Emby Views / Items JSON into <see cref="LibraryItem"/> rows.</summary>
public static class EmbyItemsParser
{
    /// <summary>Kinds Emby reports without an <c>IsFolder</c> flag that are still containers.</summary>
    private static readonly HashSet<string> FolderKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "CollectionFolder", "UserView", "Folder", "Series", "Season", "BoxSet", "Playlist", "MusicAlbum", "MusicArtist",
    };

    public static IReadOnlyList<LibraryItem> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement items = root;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("Items", out JsonElement nested)
                && nested.ValueKind == JsonValueKind.Array)
            {
                items = nested;
            }

            if (items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<LibraryItem> list = [];
            foreach (JsonElement element in items.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? id = ReadString(element, "Id");
                string? name = ReadString(element, "Name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string kind = ReadString(element, "Type") ?? "Item";
                bool isFolder = ReadBool(element, "IsFolder") ?? FolderKinds.Contains(kind);
                string? primaryTag = element.TryGetProperty("ImageTags", out JsonElement tags) && tags.ValueKind == JsonValueKind.Object
                    ? ReadString(tags, "Primary")
                    : null;
                double? playedPercentage = null;
                bool played = false;
                long? resumeTicks = null;
                if (element.TryGetProperty("UserData", out JsonElement userData) && userData.ValueKind == JsonValueKind.Object)
                {
                    playedPercentage = ReadDouble(userData, "PlayedPercentage");
                    played = ReadBool(userData, "Played") ?? false;
                    resumeTicks = ReadLong(userData, "PlaybackPositionTicks");
                }

                list.Add(new LibraryItem(
                    id,
                    name,
                    kind,
                    ReadString(element, "Overview"),
                    ReadString(element, "Path"),
                    isFolder,
                    ReadString(element, "CollectionType"),
                    ReadInt(element, "IndexNumber"),
                    ReadInt(element, "ParentIndexNumber"),
                    ReadString(element, "SeriesName"),
                    ReadInt(element, "ProductionYear"),
                    primaryTag,
                    ReadString(element, "SeriesId"),
                    ReadString(element, "SeriesPrimaryImageTag"),
                    playedPercentage,
                    played,
                    resumeTicks,
                    ReadLong(element, "RunTimeTicks"),
                    ReadString(element, "ParentId"),
                    ReadString(element, "SeasonId")));
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// One item with the detail fields (<c>Users/{id}/Items/{itemId}</c>). The
    /// listing row comes from the same JSON; streams from the first MediaSource.
    /// </summary>
    public static LibraryItemDetails? ParseDetails(string? json)
    {
        LibraryItem? item = string.IsNullOrWhiteSpace(json) ? null : Parse("[" + json + "]").FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json!);
            JsonElement root = document.RootElement;
            string? container = ReadString(root, "Container");
            List<MediaSourceInfo> versions = [];
            if (root.TryGetProperty("MediaSources", out JsonElement sources) && sources.ValueKind == JsonValueKind.Array)
            {
                int n = 0;
                foreach (JsonElement source in sources.EnumerateArray())
                {
                    n++;
                    string id = ReadString(source, "Id") ?? item.Id;
                    string name = ReadString(source, "Name") ?? (n == 1 ? item.Name : item.Name + " (" + n + ")");
                    versions.Add(new MediaSourceInfo(
                        id,
                        name,
                        ReadString(source, "Container"),
                        ReadLong(source, "Size"),
                        ReadLong(source, "Bitrate"),
                        ParseStreams(source)));
                }
            }

            MediaSourceInfo? first = versions.FirstOrDefault();
            container ??= first?.Container;
            IReadOnlyList<MediaStreamInfo> streams = first?.Streams ?? [];
            string? sourceName = first?.Name;
            long? sourceSize = first?.Size;
            long? sourceBitrate = first?.Bitrate;

            string? backdropId = null;
            IReadOnlyList<string> backdropTags = [];
            if (ReadStrings(root, "BackdropImageTags") is { Count: > 0 } own)
            {
                backdropId = item.Id;
                backdropTags = own;
            }
            else if (ReadStrings(root, "ParentBackdropImageTags") is { Count: > 0 } parent)
            {
                backdropId = ReadString(root, "ParentBackdropItemId");
                backdropTags = parent;
            }

            List<PersonInfo> people = [];
            if (root.TryGetProperty("People", out JsonElement cast) && cast.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement p in cast.EnumerateArray())
                {
                    string? id = ReadString(p, "Id");
                    string? name = ReadString(p, "Name");
                    if (id is null || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    people.Add(new PersonInfo(id, name, ReadString(p, "Role"), ReadString(p, "Type") ?? "Actor", ReadString(p, "PrimaryImageTag")));
                }
            }

            List<ExternalLink> links = [];
            if (root.TryGetProperty("ExternalUrls", out JsonElement urls) && urls.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement u in urls.EnumerateArray())
                {
                    string? name = ReadString(u, "Name");
                    string? url = ReadString(u, "Url");
                    if (!string.IsNullOrWhiteSpace(name) && Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute)
                        && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
                    {
                        links.Add(new ExternalLink(name, absolute.AbsoluteUri));
                    }
                }
            }

            bool favorite = false;
            if (root.TryGetProperty("UserData", out JsonElement userData) && userData.ValueKind == JsonValueKind.Object)
            {
                favorite = ReadBool(userData, "IsFavorite") ?? false;
            }

            return new LibraryItemDetails(
                item,
                ReadString(root, "Overview"),
                ReadStrings(root, "Taglines").FirstOrDefault(),
                ReadStrings(root, "Genres"),
                ReadNamed(root, "Studios"),
                ReadString(root, "OfficialRating"),
                ReadDouble(root, "CommunityRating"),
                ReadDate(root, "PremiereDate"),
                container,
                streams,
                backdropId,
                backdropTags.Count > 0 ? backdropTags[0] : null)
            {
                OriginalTitle = ReadString(root, "OriginalTitle"),
                Status = ReadString(root, "Status"),
                EndDate = ReadDate(root, "EndDate"),
                IsFavorite = favorite,
                Played = item.Played,
                People = people,
                ExternalUrls = links,
                BackdropTags = backdropTags,
                SourceName = sourceName,
                SourceSize = sourceSize,
                SourceBitrate = sourceBitrate,
                Sources = versions,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The <c>MediaStreams</c> of one media source, in container order.</summary>
    private static IReadOnlyList<MediaStreamInfo> ParseStreams(JsonElement source)
    {
        List<MediaStreamInfo> streams = [];
        if (!source.TryGetProperty("MediaStreams", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return streams;
        }

        int position = 0;
        foreach (JsonElement s in list.EnumerateArray())
        {
            string? type = ReadString(s, "Type");
            if (type is null)
            {
                position++;
                continue;
            }

            streams.Add(new MediaStreamInfo(
                type,
                ReadString(s, "Codec"),
                ReadString(s, "Profile"),
                ReadString(s, "Language"),
                ReadString(s, "DisplayTitle") ?? ReadString(s, "Title"),
                ReadInt(s, "Width"),
                ReadInt(s, "Height"),
                ReadString(s, "VideoRange"),
                ReadInt(s, "Channels"),
                ReadString(s, "ChannelLayout"),
                ReadBool(s, "IsDefault") ?? false,
                ReadBool(s, "IsExternal") ?? false,
                ReadLong(s, "BitRate"),
                ReadDouble(s, "AverageFrameRate") ?? ReadDouble(s, "RealFrameRate"),
                ReadInt(s, "Index") ?? position));
            position++;
        }

        return streams;
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed
            : null;

    private static IReadOnlyList<string> ReadStrings(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Where(s => s.Length > 0).ToList()
            : [];

    /// <summary>Arrays of <c>{ "Name": ... }</c> objects (Studios).</summary>
    private static IReadOnlyList<string> ReadNamed(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Object ? ReadString(v, "Name") : null).Where(s => s is not null).Select(s => s!).ToList()
            : [];

    /// <summary><c>TotalRecordCount</c> of a paged listing, when present.</summary>
    public static int? TotalCount(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ReadInt(document.RootElement, "TotalRecordCount") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool? ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n)
            ? n
            : null;

    private static long? ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n)
            ? n
            : null;

    private static double? ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double d)
            ? d
            : null;
}
