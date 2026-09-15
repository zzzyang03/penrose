namespace Penrose.Core.Playback;

/// <summary>
/// Bitstream success is <c>audio-out-params/format</c> containing
/// <c>spdif</c> or <c>iec</c>. Decoder <c>audio-params</c> is not passthrough.
/// </summary>
public static class AudioPassthrough
{
    /// <summary>mpv <c>audio-spdif</c> list while passthrough is on.</summary>
    public const string SpdifCodecs = "ac3,eac3,dts,dts-hd,truehd";

    public static bool IsSpdifFormat(string? audioOutFormat)
    {
        if (string.IsNullOrWhiteSpace(audioOutFormat))
        {
            return false;
        }

        return audioOutFormat.Contains("spdif", StringComparison.OrdinalIgnoreCase)
            || audioOutFormat.Contains("iec", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True for mpv <c>audio-codec-name</c> values covered by <see cref="SpdifCodecs"/>.
    /// The name is the FFmpeg codec: DTS-HD MA and Atmos only differ in
    /// <c>codec-profile</c>, so they are still <c>dts</c> / <c>truehd</c>.
    /// </summary>
    public static bool IsPassthroughCodec(string? audioCodecName)
    {
        if (string.IsNullOrWhiteSpace(audioCodecName))
        {
            return false;
        }

        string name = audioCodecName.Trim();
        return name.Equals("ac3", StringComparison.OrdinalIgnoreCase)
            || name.Equals("eac3", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dts", StringComparison.OrdinalIgnoreCase)
            || name.Equals("truehd", StringComparison.OrdinalIgnoreCase);
    }
}
