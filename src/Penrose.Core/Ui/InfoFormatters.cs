namespace Penrose.Core.Ui;

/// <summary>
/// Small text helpers for the playback info overlay. Pure functions, kept out
/// of MainWindow so they can be unit-tested without WinUI.
/// </summary>
public static class InfoFormatters
{
    /// <summary>
    /// Compact bitrate label: 18.4 Mbps / 640 kbps / 0 bps for &lt;1 kbps. Returns
    /// <paramref name="fallback"/> when the value is null / zero / negative so
    /// the overlay can show a dash for uninitialised / unknown values.
    /// </summary>
    public static string FormatBitrate(long? bitsPerSecond, string fallback = "—")
    {
        if (bitsPerSecond is null or <= 0)
        {
            return fallback;
        }

        double bps = bitsPerSecond.Value;
        if (bps >= 1_000_000)
        {
            return (bps / 1_000_000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " Mbps";
        }

        if (bps >= 1_000)
        {
            return (bps / 1_000d).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " kbps";
        }

        return bps.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " bps";
    }

    /// <summary>
    /// Compact file size: 4.3 GiB / 312 MiB / 921 B. Uses binary units so the
    /// number matches what <c>file-size</c> reports from mpv (1 KiB = 1024 B).
    /// </summary>
    public static string FormatBytes(long? bytes, string fallback = "—")
    {
        if (bytes is null or <= 0)
        {
            return fallback;
        }

        const long KiB = 1024L;
        const long MiB = KiB * 1024;
        const long GiB = MiB * 1024;
        const long TiB = GiB * 1024;

        double b = bytes.Value;
        if (b >= TiB)
        {
            return (b / TiB).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " TiB";
        }

        if (b >= GiB)
        {
            return (b / GiB).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " GiB";
        }

        if (b >= MiB)
        {
            return (b / MiB).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " MiB";
        }

        if (b >= KiB)
        {
            return (b / KiB).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " KiB";
        }

        return b.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " B";
    }

    /// <summary>
    /// mpv returns <c>file-format</c> as a comma-separated list of alias names
    /// ("matroska,webm" / "mov,mp4,m4a,3gp,3g2,mj2"). Take the first token so
    /// the overlay shows a single short name.
    /// </summary>
    public static string ShortContainer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "—";
        }

        int comma = raw.IndexOf(',', StringComparison.Ordinal);
        string first = comma >= 0 ? raw[..comma] : raw;
        return string.IsNullOrWhiteSpace(first) ? raw.Trim() : first.Trim();
    }

    /// <summary>
    /// Container display: <c>matroska (mkv)</c>, <c>mov (mp4)</c>, or just
    /// <c>matroska</c> when the short alias is empty.
    /// </summary>
    public static string ContainerLabel(string? raw)
    {
        string shortName = ShortContainer(raw);
        string full = raw ?? "";
        if (shortName == "—")
        {
            return "—";
        }

        int comma = full.IndexOf(',', StringComparison.Ordinal);
        string first = comma >= 0 ? full[..comma] : full;
        // "mov" and "mp4" are interchangeable; treat them as the same token so
        // "mov,mp4,m4a" shows as "mov (mp4)".
        bool same = first.Equals(shortName, StringComparison.OrdinalIgnoreCase)
            || shortName.Equals("mov", StringComparison.OrdinalIgnoreCase) && first.Equals("mp4", StringComparison.OrdinalIgnoreCase)
            || shortName.Equals("mp4", StringComparison.OrdinalIgnoreCase) && first.Equals("mov", StringComparison.OrdinalIgnoreCase);
        return same ? first : first + " (" + shortName + ")";
    }
}
