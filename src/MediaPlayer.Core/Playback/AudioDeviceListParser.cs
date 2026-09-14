using System.Text.Json;

namespace MediaPlayer.Core.Playback;

public sealed record AudioDeviceInfo(string Name, string Description);

/// <summary>
/// Parses libmpv <c>audio-device-list</c> JSON from <c>mpv_get_property_string</c>.
/// </summary>
public static class AudioDeviceListParser
{
    public static IReadOnlyList<AudioDeviceInfo> Parse(string? json)
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

            List<AudioDeviceInfo> devices = [];
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? name = ReadString(element, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                devices.Add(new AudioDeviceInfo(name, ReadString(element, "description") ?? ""));
            }

            return devices;
        }
        catch (JsonException)
        {
            return [];
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
}
