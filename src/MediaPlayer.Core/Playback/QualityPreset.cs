namespace MediaPlayer.Core.Playback;

/// <summary>Three picture tiers. Maps to mpv scaler/deband only.</summary>
public enum QualityPreset
{
    Fast,
    Balanced,
    High,
}

public static class QualityPresetOptions
{
    public static IReadOnlyDictionary<string, string> ToProperties(QualityPreset preset) =>
        preset switch
        {
            QualityPreset.Fast => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scale"] = "bilinear",
                ["cscale"] = "bilinear",
                ["dscale"] = "bilinear",
                ["deband"] = "no",
                ["dither"] = "no",
                ["correct-downscaling"] = "no",
                ["sigmoid-upscaling"] = "no",
            },
            // --dither is a choice of fruit|ordered|error-diffusion|no; "auto" belongs to
            // --dither-depth and made mpv reject the whole preset (PropertyFormat).
            QualityPreset.High => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scale"] = "ewa_lanczos",
                ["cscale"] = "ewa_lanczossharp",
                ["dscale"] = "mitchell",
                ["deband"] = "yes",
                ["dither"] = "fruit",
                ["dither-depth"] = "auto",
                ["correct-downscaling"] = "yes",
                ["sigmoid-upscaling"] = "yes",
            },
            _ => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scale"] = "lanczos",
                ["cscale"] = "lanczos",
                ["dscale"] = "hermite",
                ["deband"] = "no",
                ["dither"] = "fruit",
                ["dither-depth"] = "auto",
                ["correct-downscaling"] = "yes",
                ["sigmoid-upscaling"] = "yes",
            },
        };
}
