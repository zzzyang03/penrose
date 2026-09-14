namespace Penrose.Core.Playback;

/// <summary>
/// Source HDR from mpv <c>video-params/gamma</c>. Output pipeline is a
/// separate question.
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
}
