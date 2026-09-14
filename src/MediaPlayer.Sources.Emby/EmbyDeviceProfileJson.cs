using System.Text.Json;
using MediaPlayer.Core.Sources;

namespace MediaPlayer.Sources.Emby;

public static class EmbyDeviceProfileJson
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

    public static string PlaybackInfoBody(DeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return "{\"DeviceProfile\":" + Serialize(profile) + "}";
    }
}
