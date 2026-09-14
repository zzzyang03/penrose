namespace Penrose.Core.Capabilities;

/// <summary>
/// IINA drops hardware-decode codecs the machine cannot accelerate so FFmpeg
/// does not emit a silent "Failed setup" fallback. On Windows d3d11va, drop
/// codecs that almost never have a DXVA path (prores, ffv1, vp8).
/// </summary>
public static class HwdecCodecPolicy
{
    public const string D3d11vaSafe = "h264,vc1,hevc,vp9,av1,mpeg2";

    public static string Intersect(string? mpvList, string allowed = D3d11vaSafe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allowed);
        HashSet<string> allow = Split(allowed);
        if (allow.Count == 0)
        {
            return D3d11vaSafe;
        }

        IReadOnlyList<string> source = string.IsNullOrWhiteSpace(mpvList)
            ? Split(D3d11vaSafe).ToArray()
            : Split(mpvList).ToArray();
        List<string> kept = [];
        foreach (string codec in source)
        {
            if (allow.Contains(codec) && !kept.Contains(codec, StringComparer.OrdinalIgnoreCase))
            {
                kept.Add(codec);
            }
        }

        return kept.Count == 0 ? D3d11vaSafe : string.Join(",", kept);
    }

    private static HashSet<string> Split(string list) =>
        new(
            list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
}
