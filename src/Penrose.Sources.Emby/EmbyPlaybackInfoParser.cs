using System.Text.Json;
using Penrose.Core.Playback;
using Penrose.Core.Sources;

namespace Penrose.Sources.Emby;

public static class EmbyPlaybackInfoParser
{
    /// <param name="allowServerFilePaths">
    /// Probe <c>Protocol=File</c> paths (UNC / local) with <c>File.Exists</c>. Off by
    /// default; see <see cref="DirectPlayPath"/>.
    /// </param>
    public static PlaybackCandidate Parse(string json, Uri serverBase, string itemId, bool allowServerFilePaths = false) =>
        Parse(json, serverBase, itemId, allowServerFilePaths, preferredSourceId: null);

    /// <param name="preferredSourceId">
    /// The version the user chose on the item page; used when the server lists it
    /// with bytes, otherwise the usual first-good-version rule applies.
    /// </param>
    public static PlaybackCandidate Parse(string json, Uri serverBase, string itemId, bool allowServerFilePaths, string? preferredSourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(serverBase);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Emby PlaybackInfo is not a JSON object.");
        }

        string? session = ReadString(root, "PlaySessionId");
        if (!root.TryGetProperty("MediaSources", out JsonElement sources)
            || sources.ValueKind != JsonValueKind.Array
            || sources.GetArrayLength() == 0)
        {
            string? error = ReadString(root, "ErrorCode");
            throw new InvalidOperationException(
                error is null ? "Emby PlaybackInfo has no MediaSources." : "Emby PlaybackInfo failed: " + error);
        }

        return FromSource(PickSource(sources, preferredSourceId), serverBase, itemId, session, allowServerFilePaths);
    }

    internal static JsonElement PickSource(JsonElement sources, string? preferredSourceId)
    {
        if (!string.IsNullOrEmpty(preferredSourceId))
        {
            foreach (JsonElement source in sources.EnumerateArray())
            {
                if (string.Equals(ReadString(source, "Id"), preferredSourceId, StringComparison.Ordinal)
                    && (!source.TryGetProperty("Size", out JsonElement size) || size.ValueKind != JsonValueKind.Number || (size.TryGetInt64(out long bytes) && bytes > 0)))
                {
                    return source;
                }
            }
        }

        return PickSource(sources);
    }

    /// <summary>
    /// Items with several versions list them all; a version whose file has gone
    /// missing is reported with <c>Size</c> 0 and would 404 on the stream URL, so
    /// prefer the first version that still has bytes.
    /// </summary>
    internal static JsonElement PickSource(JsonElement sources)
    {
        JsonElement first = sources[0];
        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (!source.TryGetProperty("Size", out JsonElement size)
                || size.ValueKind != JsonValueKind.Number
                || (size.TryGetInt64(out long bytes) && bytes > 0))
            {
                return source;
            }
        }

        return first;
    }

    private static PlaybackCandidate FromSource(
        JsonElement source,
        Uri serverBase,
        string itemId,
        string? session,
        bool allowServerFilePaths)
    {
        Dictionary<string, string> headers = ReadHeaders(source);
        IReadOnlyList<ExternalSubtitle> subs = ReadExternalSubs(source, serverBase);
        string? sourceId = ReadString(source, "Id");
        string? path = ReadString(source, "Path");
        string? directUrl = ReadString(source, "DirectStreamUrl");
        string? transcodeUrl = ReadString(source, "TranscodingUrl");
        bool directPlay = ReadBool(source, "SupportsDirectPlay");
        bool directStream = ReadBool(source, "SupportsDirectStream");
        string? protocol = ReadString(source, "Protocol");

        if (directPlay && DirectPlayPath.TryOpenLocal(protocol, path, allowServerFilePaths, out Uri localFile))
        {
            return new PlaybackCandidate
            {
                Method = PlayMethod.DirectPlay,
                Uri = localFile,
                Headers = headers,
                PlaySessionId = session,
                MediaSourceId = sourceId,
                ItemId = itemId,
                ProviderId = "emby",
                ExternalSubtitles = subs,
                ServerPath = path,
                SupportsPathMapping = true,
                SourceKind = localFile.IsUnc ? MediaSourceKind.NetworkShare : MediaSourceKind.LocalFile,
            };
        }

        // Path is only a URL when it is an absolute http(s) URL (strm targets); a
        // '/'-prefixed Path is the server's Linux filesystem, not a route.
        string? remote = DirectPlayPath.LooksLikeRemoteUrl(directUrl)
            ? directUrl
            : DirectPlayPath.IsHttpUrl(path)
                ? path
                : null;
        if ((directPlay || directStream) && remote is not null)
        {
            return new PlaybackCandidate
            {
                Method = directPlay ? PlayMethod.DirectPlay : PlayMethod.DirectStream,
                Uri = ResolveUrl(serverBase, remote),
                Headers = headers,
                PlaySessionId = session,
                MediaSourceId = sourceId,
                ItemId = itemId,
                ProviderId = "emby",
                ExternalSubtitles = subs,
                ServerPath = path,
                SupportsPathMapping = DirectPlayPath.LooksLikeFile(protocol, path),
                SourceKind = MediaSourceKind.ServerDirectPlay,
            };
        }

        if (!string.IsNullOrWhiteSpace(transcodeUrl))
        {
            return new PlaybackCandidate
            {
                Method = PlayMethod.Transcode,
                Uri = ResolveUrl(serverBase, transcodeUrl),
                Headers = headers,
                PlaySessionId = session,
                MediaSourceId = sourceId,
                ItemId = itemId,
                ProviderId = "emby",
                ExternalSubtitles = subs,
                SourceKind = MediaSourceKind.ServerTranscode,
            };
        }

        throw new InvalidOperationException("Emby MediaSource has no playable URL.");
    }

    private static Uri ResolveUrl(Uri serverBase, string? relativeOrAbsolute)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
        {
            throw new InvalidOperationException("Empty media URL.");
        }

        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out Uri? absolute))
        {
            return absolute;
        }

        return new Uri(serverBase, relativeOrAbsolute);
    }

    private static Dictionary<string, string> ReadHeaders(JsonElement source)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        if (!source.TryGetProperty("RequiredHttpHeaders", out JsonElement obj)
            || obj.ValueKind != JsonValueKind.Object)
        {
            return headers;
        }

        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                headers[property.Name] = property.Value.GetString() ?? "";
            }
        }

        return headers;
    }

    private static List<ExternalSubtitle> ReadExternalSubs(JsonElement source, Uri serverBase)
    {
        List<ExternalSubtitle> list = [];
        if (!source.TryGetProperty("MediaStreams", out JsonElement streams)
            || streams.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement stream in streams.EnumerateArray())
        {
            if (!string.Equals(ReadString(stream, "Type"), "Subtitle", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ReadBool(stream, "IsExternal"))
            {
                continue;
            }

            // Only the server's delivery route. Falling back to the stream's Path
            // would turn a server filesystem path into file:/// or a UNC probe.
            string? path = ReadString(stream, "DeliveryUrl");
            string? method = ReadString(stream, "DeliveryMethod");
            if (string.IsNullOrWhiteSpace(path)
                || (method is not null && !method.Equals("External", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            list.Add(new ExternalSubtitle(
                ResolveUrl(serverBase, path),
                ReadString(stream, "Language"),
                ReadString(stream, "DisplayTitle") ?? ReadString(stream, "Title"),
                Encoding: null));
        }

        return list;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();
}
