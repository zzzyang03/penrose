using System.Text.Json;
using Penrose.Core.Sources;

namespace Penrose.Sources.Jellyfin;

public static class JellyfinDeviceProfileJson
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true,
    };

    public static string Serialize(DeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var payload = new
        {
            profile.Name,
            MaxStreamingBitrate = profile.MaxStreamingBitrate,
            MaxStaticBitrate = profile.MaxStreamingBitrate,
            DirectPlayProfiles = profile.DirectPlay.Select(p => new
            {
                p.Container,
                p.Type,
                p.VideoCodec,
                p.AudioCodec,
            }).ToArray(),
            TranscodingProfiles = profile.Transcode.Select(p => new
            {
                p.Container,
                p.Type,
                p.VideoCodec,
                p.AudioCodec,
                p.Protocol,
                Context = "Streaming",
            }).ToArray(),
            SubtitleProfiles = profile.Subtitles.Select(p => new
            {
                p.Format,
                p.Method,
            }).ToArray(),
        };
        return JsonSerializer.Serialize(payload, Json);
    }
}
