namespace Penrose.Core.Playback;

/// <summary>
/// Source HDR from mpv <c>video-params/gamma</c> and, for Dolby Vision
/// profile 5 (IPTPQc2, typical of AMZN WEB-DL), <c>dolby-vision-profile</c>.
/// Output pipeline is a separate question.
/// </summary>
public static class HdrSource
{
    public static bool IsTransferHdr(string? gamma)
    {
        if (string.IsNullOrWhiteSpace(gamma))
        {
            return false;
        }

        return gamma.Equals("pq", StringComparison.OrdinalIgnoreCase)
            || gamma.Equals("hlg", StringComparison.OrdinalIgnoreCase)
            || gamma.Equals("st2084", StringComparison.OrdinalIgnoreCase)
            || gamma.Equals("pq-hlg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Dolby Vision is HDR even when gamma is still empty or not PQ (profile 5).
    /// </summary>
    public static bool IsSourceHdr(string? gamma, int? dolbyVisionProfile = null) =>
        dolbyVisionProfile is > 0 || IsTransferHdr(gamma);
}
