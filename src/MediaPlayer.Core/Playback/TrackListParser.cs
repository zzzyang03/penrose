using System.Text.Json;

namespace MediaPlayer.Core.Playback;

/// <summary>
/// Parses libmpv <c>track-list</c> JSON from <c>mpv_get_property_string</c>.
/// </summary>
public static class TrackListParser
{
    public static IReadOnlyList<TrackInfo> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        string trimmed = json.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '[')
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<TrackInfo> tracks = [];
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                long id = ReadInt64(element, "id");
                string type = ReadString(element, "type") ?? "";
                if (id <= 0 || string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                long channels = ReadInt64(element, "demux-channel-count");
                if (channels <= 0)
                {
                    channels = ReadInt64(element, "audio-channels");
                }

                long dolbyVision = ReadInt64(element, "dolby-vision-profile");
                long width = ReadInt64(element, "demux-w");
                long height = ReadInt64(element, "demux-h");
                tracks.Add(new TrackInfo(
                    id,
                    type,
                    ReadString(element, "lang"),
                    ReadString(element, "title"),
                    ReadBool(element, "selected"),
                    ReadBool(element, "external"),
                    Codec: ReadString(element, "codec"),
                    CodecProfile: ReadString(element, "codec-profile"),
                    Channels: channels > 0 ? (int)channels : null,
                    ChannelLayout: ReadString(element, "demux-channels"),
                    DolbyVisionProfile: dolbyVision > 0 ? (int)dolbyVision : null,
                    Width: width > 0 ? (int)width : null,
                    Height: height > 0 ? (int)height : null));
            }

            return tracks;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IEnumerable<TrackInfo> OfType(IEnumerable<TrackInfo> tracks, string type)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return tracks.Where(track => string.Equals(track.Type, type, StringComparison.Ordinal));
    }

    private static long ReadInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out long number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out long parsed) => parsed,
            _ => 0,
        };
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

    private static bool ReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString() is "yes" or "true",
            JsonValueKind.Number => value.TryGetInt64(out long number) && number != 0,
            _ => false,
        };
    }
}
