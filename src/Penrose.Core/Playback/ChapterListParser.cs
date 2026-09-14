using System.Text.Json;

namespace Penrose.Core.Playback;

/// <summary>
/// Parses libmpv <c>chapter-list</c> JSON from <c>mpv_get_property_string</c>.
/// </summary>
public static class ChapterListParser
{
    public static IReadOnlyList<ChapterInfo> Parse(string? json)
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

            List<ChapterInfo> chapters = [];
            int index = 0;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                double seconds = ReadDouble(element, "time");
                if (seconds < 0)
                {
                    continue;
                }

                string? title = ReadString(element, "title");
                chapters.Add(new ChapterInfo(index, title, TimeSpan.FromSeconds(seconds)));
                index++;
            }

            return chapters;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static double ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return -1;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out double number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed) => parsed,
            _ => -1,
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
}
