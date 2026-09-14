namespace MediaPlayer.Core.Playback;

/// <summary>
/// What the player knows about the current file after FILE_LOADED: mpv
/// <c>video-params</c> plus the selected video / audio tracks from <c>track-list</c>.
/// </summary>
public sealed record MediaFormatInfo(
    string? Gamma,
    string? Primaries,
    int? Width,
    int? Height,
    TrackInfo? Video,
    TrackInfo? Audio,
    /// <summary>mpv exposes <c>video-params/scene-max-*</c> only when HDR10+ metadata is present.</summary>
    bool Hdr10Plus = false);

/// <summary>Transfer / metadata class of the current video, for the permanent "HDR" indicator.</summary>
public enum DynamicRange
{
    Sdr,
    Hdr10,
    Hdr10Plus,
    Hlg,
    DolbyVision,
}

/// <summary>
/// The premium-format chips shown briefly when a file starts. Only Dolby Vision,
/// HDR10+, Dolby Atmos and DTS:X get a chip; plain HDR10 / HLG, resolution and
/// ordinary codecs do not (HDR is signalled by the permanent indicator instead).
/// Pure function over <see cref="MediaFormatInfo"/>; the names are the trade
/// names FFmpeg reports in <c>codec-profile</c>.
/// </summary>
public static class FormatBadges
{
    public const string DolbyVision = "Dolby Vision";
    public const string Hdr10Plus = "HDR10+";
    public const string DolbyAtmos = "Dolby Atmos";
    public const string DtsX = "DTS:X";

    public static readonly IReadOnlyList<string> All = [DolbyVision, Hdr10Plus, DolbyAtmos, DtsX];

    public static IReadOnlyList<string> For(MediaFormatInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        List<string> badges = [];

        switch (RangeOf(info))
        {
            case DynamicRange.DolbyVision:
                badges.Add(DolbyVision);
                break;
            case DynamicRange.Hdr10Plus:
                badges.Add(Hdr10Plus);
                break;
        }

        string profile = info.Audio?.CodecProfile ?? "";
        if (profile.Contains("Atmos", StringComparison.OrdinalIgnoreCase))
        {
            badges.Add(DolbyAtmos);
        }
        else if (profile.Contains("DTS:X", StringComparison.OrdinalIgnoreCase))
        {
            badges.Add(DtsX);
        }

        return badges;
    }

    /// <summary>
    /// Dolby Vision wins (its base layer is PQ or IPTPQc2), then HDR10+ (dynamic
    /// metadata on a PQ stream), then the plain transfers.
    /// </summary>
    public static DynamicRange RangeOf(MediaFormatInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.Video?.DolbyVisionProfile is > 0)
        {
            return DynamicRange.DolbyVision;
        }

        if (info.Hdr10Plus)
        {
            return DynamicRange.Hdr10Plus;
        }

        if (IsPq(info.Gamma))
        {
            return DynamicRange.Hdr10;
        }

        return IsHlg(info.Gamma) ? DynamicRange.Hlg : DynamicRange.Sdr;
    }

    /// <summary>Short display name for the indicator tooltip.</summary>
    public static string Describe(DynamicRange range) => range switch
    {
        DynamicRange.DolbyVision => DolbyVision,
        DynamicRange.Hdr10Plus => Hdr10Plus,
        DynamicRange.Hdr10 => "HDR10",
        DynamicRange.Hlg => "HLG",
        _ => "SDR",
    };

    public static bool IsPq(string? gamma) =>
        gamma is not null && (gamma.Equals("pq", StringComparison.OrdinalIgnoreCase)
            || gamma.Equals("smpte2084", StringComparison.OrdinalIgnoreCase)
            || gamma.Equals("st2084", StringComparison.OrdinalIgnoreCase));

    public static bool IsHlg(string? gamma) =>
        gamma is not null && (gamma.Equals("hlg", StringComparison.OrdinalIgnoreCase)
            || gamma.Equals("arib-std-b67", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// mpv channel layout strings (<c>audio-params/hr-channels</c>, track
/// <c>demux-channels</c>) to the short labels users know, and the
/// <c>--audio-channels</c> values the downmix picker can set.
/// </summary>
public static class ChannelLayouts
{
    /// <summary>Follow the audio policy (no override).</summary>
    public const string FollowPolicy = "";

    /// <summary>Keep the source layout (mpv <c>auto</c>).</summary>
    public const string Source = "auto";

    public static readonly IReadOnlyList<string> DownmixOptions = ["stereo", "2.1", "5.1", "7.1"];

    public static string? Label(string? layout, int? channels = null)
    {
        if (string.IsNullOrWhiteSpace(layout))
        {
            return channels switch
            {
                null or <= 0 => null,
                1 => "1.0",
                2 => "2.0",
                6 => "5.1",
                8 => "7.1",
                int n => n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ch",
            };
        }

        string core = layout.Trim();
        int paren = core.IndexOf('(', StringComparison.Ordinal);
        if (paren > 0)
        {
            core = core[..paren];
        }

        return core.ToLowerInvariant() switch
        {
            "mono" => "1.0",
            "stereo" => "2.0",
            "quad" => "4.0",
            "hexagonal" => "6.0",
            "octagonal" => "8.0",
            "" => Label(null, channels),
            _ => core,
        };
    }

    /// <summary>"7.1 → 5.1" when the output layout differs from the source, otherwise the single label.</summary>
    public static string? Describe(string? sourceLayout, int? sourceChannels, string? outputLayout, int? outputChannels)
    {
        string? source = Label(sourceLayout, sourceChannels);
        string? output = Label(outputLayout, outputChannels);
        if (source is null)
        {
            return output;
        }

        if (output is null || string.Equals(source, output, StringComparison.Ordinal))
        {
            return source;
        }

        return source + " \u2192 " + output;
    }

    public static bool IsValidOverride(string? value) =>
        string.IsNullOrEmpty(value)
        || value.Equals(Source, StringComparison.OrdinalIgnoreCase)
        || DownmixOptions.Contains(value, StringComparer.OrdinalIgnoreCase);
}
