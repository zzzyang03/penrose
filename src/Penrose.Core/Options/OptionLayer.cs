namespace Penrose.Core.Options;

public enum OptionLayer
{
    EngineBootstrap,
    SurfaceBootstrap,
    PlaybackPolicy,
    UserAdvanced,
}

public sealed record OptionValidationResult(string Key, bool Accepted, string? RejectionReason)
{
    public static OptionValidationResult Ok(string key) => new(key, true, null);

    public static OptionValidationResult Reject(string key, string reason) =>
        new(key, false, reason);
}

/// <summary>
/// Four layers (engine bootstrap, surface bootstrap, playback policy, user
/// advanced) own disjoint keys. Keys outside the whitelist are rejected
/// with a visible reason; they are never silently ignored.
/// </summary>
public static class OptionWhitelist
{
    public static readonly IReadOnlySet<string> EngineBootstrap = new HashSet<string>(StringComparer.Ordinal)
    {
        "vo", "gpu-api", "hwdec", "hwdec-codecs", "config", "terminal", "osc", "osd-bar",
        "input-default-bindings", "input-vo-keyboard", "idle", "keep-open", "load-scripts", "ytdl",
        "force-window", "geometry", "border", "ontop", "keepaspect-window", "vf", "fullscreen",
    };

    public static readonly IReadOnlySet<string> SurfaceBootstrap = new HashSet<string>(StringComparer.Ordinal)
    {
        "gpu-context", "d3d11-output-mode", "d3d11-output-format", "d3d11-output-csp",
        "d3d11-composition-size", "d3d11-adapter", "wid", "d3d11-flip",
        "target-colorspace-hint", "target-colorspace-hint-mode", "target-colorspace-hint-strict",
        "target-peak", "target-prim", "target-trc", "target-gamut",
        "display-fps-override",
    };

    public static readonly IReadOnlySet<string> PlaybackPolicy = new HashSet<string>(StringComparer.Ordinal)
    {
        "tone-mapping", "hdr-compute-peak", "gamut-mapping-mode",
        "sub-hdr-peak", "image-subs-hdr-peak", "blend-subtitles",
        "ao", "audio-device", "audio-channels", "audio-exclusive", "audio-spdif",
        "audio-delay", "af",
        "sub-font", "sub-font-provider", "sub-fonts-dir", "sub-ass-override",
        "sub-auto", "sub-file-paths", "sub-delay", "sub-pos", "sub-codepage",
        "cache", "demuxer-max-bytes", "demuxer-readahead-secs",
        "loop", "speed",
        // Network credentials (http-header-fields, user-agent, cookies) are
        // file-local loadfile options only; TLS/proxy keys are never user-settable.
        // Neither group belongs to any layer.
    };

    /// <summary>
    /// Non-structural keys users may set after the advanced toggle.
    /// Structural keys from Engine/Surface layers are always rejected here.
    /// </summary>
    public static readonly IReadOnlySet<string> UserAdvanced = new HashSet<string>(StringComparer.Ordinal)
    {
        "deband", "deband-iterations", "deband-threshold", "deband-range", "deband-grain",
        "scale", "cscale", "dscale", "linear-downscaling", "correct-downscaling",
        "sigmoid-upscaling", "dither-depth", "dither",
        "video-sync", "interpolation", "tscale",
        "volume", "volume-max", "mute",
        "sub-scale", "sub-color", "sub-border-color", "sub-shadow-color",
        "hwdec", "d3d11va-zero-copy",
        "vd-lavc-dr", "hr-seek", "hr-seek-framedrop",
        "audio-pitch-correction", "audio-normalize-downmix",
    };

    public static OptionValidationResult Validate(OptionLayer layer, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        bool allowed = layer switch
        {
            OptionLayer.EngineBootstrap => EngineBootstrap.Contains(key),
            OptionLayer.SurfaceBootstrap => SurfaceBootstrap.Contains(key),
            OptionLayer.PlaybackPolicy => PlaybackPolicy.Contains(key),
            OptionLayer.UserAdvanced => UserAdvanced.Contains(key)
                && !EngineBootstrap.Contains(key)
                && !IsStructuralSurfaceKey(key),
            _ => false,
        };

        if (allowed)
        {
            return OptionValidationResult.Ok(key);
        }

        string owner = DescribeOwner(key);
        return OptionValidationResult.Reject(
            key,
            $"Option '{key}' is not allowed on {layer}. {owner}");
    }

    public static bool IsNetworkCredentialKey(string key) =>
        key is "http-header-fields" or "user-agent" or "cookies" or "cookies-file"
            or "http-header-fields-cookies" or "stream-lavf-o" or "referrer" or "http-proxy";

    /// <summary>
    /// Keys that weaken transport security or run external programs. Rejected on
    /// every layer regardless of whitelist membership.
    /// </summary>
    public static bool IsSecuritySensitiveKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key is "tls-verify" or "tls-ca-file" or "tls-cert-file" or "tls-key-file"
                or "ytdl" or "ytdl-format" or "ytdl-raw-options" or "ytdl-path"
                or "script" or "scripts" or "script-opts" or "load-scripts"
                or "input-conf" or "input-ipc-server" or "input-ipc-client"
                or "config" or "config-dir" or "include" or "log-file" or "dump-stats"
                or "screenshot-directory" or "screenshot-template"
            || key.StartsWith("script-opts-", StringComparison.Ordinal)
            || key.StartsWith("tls-", StringComparison.Ordinal);
    }

    private static bool IsStructuralSurfaceKey(string key) =>
        SurfaceBootstrap.Contains(key);

    private static string DescribeOwner(string key)
    {
        if (IsNetworkCredentialKey(key))
        {
            return "Network credentials are file-local loadfile options and are never read from mpv.conf.";
        }

        if (IsSecuritySensitiveKey(key))
        {
            return "It affects transport security or runs external programs and is not user-settable.";
        }

        if (EngineBootstrap.Contains(key))
        {
            return "It belongs to EngineBootstrapOptions (Playback.Mpv only).";
        }

        if (SurfaceBootstrap.Contains(key))
        {
            return "It belongs to SurfaceBootstrapOptions (IVideoSurface only).";
        }

        if (PlaybackPolicy.Contains(key))
        {
            return "It belongs to PlaybackPolicyOptions.";
        }

        return "Unknown keys are rejected rather than ignored.";
    }
}
