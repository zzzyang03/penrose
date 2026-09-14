namespace Penrose.Core.Playback;

/// <summary>
/// Bitstream success is <c>audio-out-params/format</c> containing
/// <c>spdif</c> or <c>iec</c>. Decoder <c>audio-params</c> is not passthrough.
/// </summary>
public static class AudioPassthrough
{
    public static bool IsSpdifFormat(string? audioOutFormat)
    {
        if (string.IsNullOrWhiteSpace(audioOutFormat))
        {
            return false;
        }

        return audioOutFormat.Contains("spdif", StringComparison.OrdinalIgnoreCase)
            || audioOutFormat.Contains("iec", StringComparison.OrdinalIgnoreCase);
    }
}
