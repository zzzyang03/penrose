using Penrose.Core.Options;

namespace Penrose.VideoSurface;

public enum ActiveColorMode
{
    Sdr,
    Hdr,
    WindowsWideColor,
}

public sealed record DisplayColorCapabilities(
    long AdapterLuid,
    ActiveColorMode ActiveColorMode,
    string? OutputColorSpace,
    double MaxLuminance,
    double MinLuminance,
    double MaxFullFrameLuminance,
    string? ContainerPrimaries,
    string? EffectiveGamut,
    double SdrWhiteLevel)
{
    /// <summary>
    /// Measured: DXGI GetDesc1 max-nits on the reference internal panel jumped
    /// 474 → 7303. Values outside this window are not a panel peak.
    /// </summary>
    public const double MinPlausiblePeakNits = 80;

    public const double MaxPlausiblePeakNits = 4000;

    public const double SdrFallbackNits = 203;

    public const double HdrFallbackNits = 400;

    /// <summary>
    /// Any Advanced Color desktop (HDR or wide-gamut). Sufficient for the windowed
    /// scRGB FP16 pipeline, which any Advanced Color desktop can present.
    /// </summary>
    public bool IsAdvancedColor =>
        ActiveColorMode is ActiveColorMode.Hdr or ActiveColorMode.WindowsWideColor;

    /// <summary>
    /// Windows HDR is actually on. Required for the HDR10 PQ top-level pipeline:
    /// a WCG-only desktop does not accept RGB_FULL_G2084_NONE_P2020.
    /// </summary>
    public bool IsHdr => ActiveColorMode == ActiveColorMode.Hdr;

    /// <summary>
    /// This mapping is not yet verified on HDR hardware. hint-strict still treats
    /// these as hints, not as the negotiated swapchain.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToTargetOptions(string outputPipeline)
    {
        string trc = outputPipeline switch
        {
            OutputPipeline.Hdr10 => "pq",
            OutputPipeline.ScRgb => "linear",
            _ => "srgb",
        };
        string prim = outputPipeline == OutputPipeline.Hdr10 ? "bt.2020" : "bt.709";
        Dictionary<string, string> options = new(StringComparer.Ordinal)
        {
            ["target-peak"] = PeakNitsFor(outputPipeline)
                .ToString("0", System.Globalization.CultureInfo.InvariantCulture),
            ["target-prim"] = ContainerPrimaries ?? prim,
            ["target-trc"] = trc,
        };
        if (!string.IsNullOrWhiteSpace(EffectiveGamut))
        {
            options["target-gamut"] = EffectiveGamut;
        }

        return options;
    }

    public double PeakNitsFor(string outputPipeline) =>
        outputPipeline == OutputPipeline.Sdr ? SdrFallbackNits : SanitizedPeakNits();

    public double SanitizedPeakNits()
    {
        if (IsPlausible(MaxLuminance))
        {
            return Math.Round(MaxLuminance);
        }

        if (IsPlausible(MaxFullFrameLuminance))
        {
            return Math.Round(MaxFullFrameLuminance);
        }

        return ActiveColorMode == ActiveColorMode.Hdr ? HdrFallbackNits : SdrFallbackNits;
    }

    public static bool IsPlausible(double nits) =>
        !double.IsNaN(nits)
        && !double.IsInfinity(nits)
        && nits >= MinPlausiblePeakNits
        && nits <= MaxPlausiblePeakNits;
}

/// <summary>Named pipelines. Windowed PQ is not a product path.</summary>
public static class OutputPipeline
{
    public const string Sdr = "sdr";
    public const string ScRgb = "scrgb";
    public const string Hdr10 = "hdr10";

    /// <summary>
    /// Composition pipeline for a window. Focus is irrelevant: DWM composes
    /// scRGB for any visible window on an Advanced Color desktop.
    /// </summary>
    public static string Windowed(bool displayAdvancedColor) => displayAdvancedColor ? ScRgb : Sdr;

    public static SurfaceBootstrapOptions Apply(
        SurfaceBootstrapOptions options,
        string pipeline,
        DisplayColorCapabilities? display = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        string peak = PeakNits(pipeline, display);
        string? gamut = display?.EffectiveGamut;
        string? primaries = display?.ContainerPrimaries;
        return pipeline switch
        {
            Hdr10 => options with
            {
                D3d11OutputFormat = "rgb10_a2",
                D3d11OutputCsp = "pq",
                TargetColorspaceHint = "yes",
                TargetTrc = "pq",
                TargetPrim = primaries ?? "bt.2020",
                TargetPeak = peak,
                TargetGamut = gamut,
            },
            ScRgb => options with
            {
                D3d11OutputFormat = "rgba16f",
                D3d11OutputCsp = "linear",
                TargetColorspaceHint = "yes",
                TargetTrc = "linear",
                TargetPrim = primaries ?? "bt.709",
                TargetPeak = peak,
                TargetGamut = gamut,
            },
            _ => options with
            {
                D3d11OutputFormat = "rgba8",
                D3d11OutputCsp = "srgb",
                TargetColorspaceHint = "no",
                TargetTrc = "srgb",
                TargetPrim = "bt.709",
                TargetPeak = DisplayColorCapabilities.SdrFallbackNits.ToString(
                    "0",
                    System.Globalization.CultureInfo.InvariantCulture),
                TargetGamut = null,
            },
        };
    }

    public static string PeakNits(string pipeline, DisplayColorCapabilities? display)
    {
        if (pipeline == Sdr)
        {
            return DisplayColorCapabilities.SdrFallbackNits.ToString(
                "0",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        if (display is null)
        {
            return DisplayColorCapabilities.HdrFallbackNits.ToString(
                "0",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return display.PeakNitsFor(pipeline).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed record SurfaceCapabilities(
    bool CanOverlayUi,
    bool SupportsWindowedHdr,
    bool SupportsFullscreenHdrMetadata,
    bool SupportsPictureInPicture,
    bool SupportsIndependentFlip,
    bool RequiresExternalColorSpaceManagement,
    bool HandlesInput);
