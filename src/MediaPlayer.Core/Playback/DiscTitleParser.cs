using System.Text.Json;

namespace MediaPlayer.Core.Playback;

public sealed record DiscTitleInfo(int Id, string Label, TimeSpan? Duration);

/// <summary>
/// Parses mpv <c>disc-titles</c> / <c>disc-title-list</c> for ISO/BDMV/DVD.
/// v3 §1.3 scheme A: user picks a title; no BD-J menus.
/// </summary>
public static class DiscTitleParser
{
    public static IReadOnlyList<DiscTitleInfo> Parse(string? jsonOrCount)
    {
        if (string.IsNullOrWhiteSpace(jsonOrCount))
        {
            return [];
        }

        string trimmed = jsonOrCount.Trim();
        if (int.TryParse(trimmed, out int count) && count > 0)
        {
            return Enumerable.Range(1, Math.Min(count, 99))
                .Select(id => new DiscTitleInfo(id, "Title " + id, null))
                .ToArray();
        }

        if (trimmed.Contains(',', StringComparison.Ordinal) && trimmed[0] != '[')
        {
            List<DiscTitleInfo> numbered = [];
            foreach (string part in trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out int id) && id > 0)
                {
                    numbered.Add(new DiscTitleInfo(id, "Title " + id, null));
                }
            }

            return numbered;
        }

        if (trimmed[0] != '[')
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

            List<DiscTitleInfo> titles = [];
            int fallback = 1;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int number) && number > 0)
                {
                    titles.Add(new DiscTitleInfo(number, "Title " + number, null));
                    continue;
                }

                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                int id = ReadInt(element, "id");
                if (id <= 0)
                {
                    id = fallback;
                }

                string? title = ReadString(element, "title") ?? ReadString(element, "name");
                double seconds = ReadDouble(element, "length");
                if (seconds < 0)
                {
                    seconds = ReadDouble(element, "duration");
                }

                TimeSpan? duration = seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;
                string label = string.IsNullOrWhiteSpace(title) ? "Title " + id : title;
                if (duration is { } time)
                {
                    label += "  (" + Format(time) + ")";
                }

                titles.Add(new DiscTitleInfo(id, label, duration));
                fallback = id + 1;
            }

            return titles;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out int parsed) => parsed,
            _ => 0,
        };
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

    private static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
}
