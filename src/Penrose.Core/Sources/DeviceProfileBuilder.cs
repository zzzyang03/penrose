using Penrose.Core.Capabilities;

namespace Penrose.Core.Sources;

/// <summary>
/// Runtime DeviceProfile. Never a static table: the product host calls
/// <see cref="FromRuntime"/> with what it knows about this session; <see cref="ReferenceHost"/>
/// is a fixture for tests only.
/// </summary>
public static class DeviceProfileBuilder
{
    public const string ClientName = "Penrose";

    /// <summary>
    /// Snapshot of the current session. The layout follows the user's PCM policy,
    /// passthrough advertises the IEC61937 codecs mpv is told to pass through,
    /// display state comes from the surface, and the hwdec method from mpv.
    /// </summary>
    public static PlaybackCapabilitySnapshot FromRuntime(
        Penrose.Core.Playback.AudioPolicy audioPolicy,
        bool audioPassthrough,
        string? audioDevice,
        bool displayIsHdr,
        string? hwdecCurrent,
        long? gpuAdapterLuid)
    {
        return new PlaybackCapabilitySnapshot
        {
            DecoderNames =
            [
                "h264", "hevc", "av1", "vp9", "mpeg2video", "vc1",
                "aac", "mp3", "flac", "alac", "opus", "vorbis", "pcm",
                "ac3", "eac3", "truehd", "dts",
            ],
            HwdecMethods = string.IsNullOrWhiteSpace(hwdecCurrent) || hwdecCurrent is "no" or "none"
                ? []
                : [hwdecCurrent],
            DisplayIsHdr = displayIsHdr,
            HdrOutputPipeline = displayIsHdr ? "scrgb" : "sdr",
            AudioDevice = string.IsNullOrWhiteSpace(audioDevice) || audioDevice.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : audioDevice,
            AudioLayout = audioPolicy switch
            {
                Penrose.Core.Playback.AudioPolicy.ForceStereo => "stereo",
                Penrose.Core.Playback.AudioPolicy.HomeTheaterPcm => "7.1",
                _ => "auto",
            },
            BitstreamAllowed = audioPassthrough,
            BitstreamCodecs = audioPassthrough ? ["ac3", "eac3", "dts", "truehd"] : [],
            SoftwareDecodingAllowed = true,
            UncReachable = false,
            GpuAdapterLuid = gpuAdapterLuid,
        };
    }

    /// <summary>Test fixture for the reference verification host. Not for the product path.</summary>
    public static PlaybackCapabilitySnapshot ReferenceHost() => new()
    {
        DecoderNames =
        [
            "h264", "hevc", "av1", "vp9", "mpeg2video", "vc1",
            "aac", "mp3", "flac", "alac", "opus", "vorbis", "pcm",
            "ac3", "eac3", "truehd", "dts",
        ],
        HwdecMethods = ["d3d11va", "nvdec-copy"],
        DisplayIsHdr = true,
        HdrOutputPipeline = "scrgb",
        AudioDevice = "wasapi/{76bae192-b153-4ad9-94a3-78b36e12c5a0}",
        AudioLayout = "stereo",
        BitstreamAllowed = true,
        BitstreamCodecs = ["ac3", "eac3", "truehd"],
        SoftwareDecodingAllowed = true,
        UncReachable = false,
        GpuAdapterLuid = unchecked((long)0xFE9E),
    };

    public static DeviceProfile Build(PlaybackCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string videoCodecs = Join(VideoCodecs(snapshot));
        string audioCodecs = Join(AudioCodecs(snapshot));
        List<DirectPlayProfile> direct =
        [
            new("Video", "mkv,webm", videoCodecs, audioCodecs),
            new("Video", "mp4,m4v,mov", videoCodecs, audioCodecs),
            new("Video", "ts,mpegts,m2ts,mpg,mpeg", videoCodecs, audioCodecs),
            new("Video", "avi,wmv,asf,flv", videoCodecs, audioCodecs),
            new("Audio", "mp3,aac,m4a,flac,ogg,opus,wav,ac3,eac3,dts,mka", VideoCodec: null, audioCodecs),
        ];

        List<TranscodeProfile> transcode =
        [
            new("Video", "ts", "h264", "aac", "hls"),
            new("Audio", "mp3", VideoCodec: null, "mp3", "http"),
        ];

        List<SubtitleDelivery> subs = [];
        foreach (string format in snapshot.LocalSubtitleFormats)
        {
            subs.Add(new SubtitleDelivery(format, "Embed"));
            if (format is "ass" or "ssa" or "srt" or "vtt")
            {
                subs.Add(new SubtitleDelivery(format, "External"));
            }
        }

        return new DeviceProfile(ClientName, direct, transcode, subs);
    }

    private static IEnumerable<string> VideoCodecs(PlaybackCapabilitySnapshot snapshot)
    {
        yield return "h264";
        yield return "hevc";
        if (snapshot.SoftwareDecodingAllowed)
        {
            yield return "av1";
            yield return "vp9";
            yield return "mpeg2video";
            yield return "vc1";
        }
    }

    private static IEnumerable<string> AudioCodecs(PlaybackCapabilitySnapshot snapshot)
    {
        yield return "aac";
        yield return "mp3";
        yield return "flac";
        yield return "alac";
        yield return "opus";
        yield return "vorbis";
        // Emby names PCM by sample format, not "pcm".
        yield return "pcm_s16le";
        yield return "pcm_s24le";
        yield return "pcm_bluray";
        yield return "pcm_dvd";
        yield return "ac3";
        yield return "eac3";
        if (snapshot.SoftwareDecodingAllowed || Contains(snapshot.BitstreamCodecs, "truehd"))
        {
            yield return "truehd";
        }

        if (snapshot.SoftwareDecodingAllowed)
        {
            yield return "dts";
        }
    }

    private static bool Contains(IReadOnlyList<string> list, string value) =>
        list.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));

    private static string Join(IEnumerable<string> values) => string.Join(',', values);
}
