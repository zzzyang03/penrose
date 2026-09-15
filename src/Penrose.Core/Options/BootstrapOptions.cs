namespace Penrose.Core.Options;

public sealed record EngineBootstrapOptions
{
    public string Vo { get; init; } = "gpu-next";
    public string GpuApi { get; init; } = "d3d11";
    public string Hwdec { get; init; } = "auto";
    public bool Config { get; init; }
    public bool Osc { get; init; }
    public bool OsdBar { get; init; }
    public bool InputDefaultBindings { get; init; }
    public bool InputVoKeyboard { get; init; }
    public bool Idle { get; init; } = true;
    public bool KeepOpen { get; init; } = true;
    public bool ForceWindow { get; init; }
    public string? Geometry { get; init; }
    /// <summary>Dolby Vision FEL stays off until a sample pass exists.</summary>
    public bool DvEnhancementLayer { get; init; }
    /// <summary>
    /// Built-in Lua scripts (osc, ytdl_hook, stats, console). Off: the host draws
    /// its own UI, and ytdl_hook would otherwise spawn yt-dlp from PATH with an
    /// untrusted URL whenever an http open fails.
    /// </summary>
    public bool LoadScripts { get; init; }

    /// <summary>
    /// mpv log level forwarded to the app log ("warn" by default; "v" or "debug"
    /// to trace stream / demuxer activity). Not an mpv property; consumed by the engine.
    /// </summary>
    public string MpvLogLevel { get; init; } = "warn";

    public IReadOnlyDictionary<string, string> ToProperties()
    {
        Dictionary<string, string> properties = new(StringComparer.Ordinal)
        {
            ["vo"] = Vo,
            ["gpu-api"] = GpuApi,
            ["hwdec"] = Hwdec,
            ["config"] = Config ? "yes" : "no",
            ["osc"] = Osc ? "yes" : "no",
            ["osd-bar"] = OsdBar ? "yes" : "no",
            ["input-default-bindings"] = InputDefaultBindings ? "yes" : "no",
            ["input-vo-keyboard"] = InputVoKeyboard ? "yes" : "no",
            ["idle"] = Idle ? "yes" : "no",
            ["keep-open"] = KeepOpen ? "yes" : "no",
            ["terminal"] = "no",
            ["force-window"] = ForceWindow ? "yes" : "no",
            ["load-scripts"] = LoadScripts ? "yes" : "no",
            ["ytdl"] = "no",
        };
        if (!string.IsNullOrWhiteSpace(Geometry))
        {
            properties["geometry"] = Geometry;
        }

        if (DvEnhancementLayer)
        {
            properties["vf"] = "format=enhancement-layer=yes";
        }

        return properties;
    }
}

public sealed record SurfaceBootstrapOptions
{
    public string GpuContext { get; init; } = "d3d11";
    public string? D3d11OutputMode { get; init; }
    public string? D3d11OutputFormat { get; init; }
    public string? D3d11OutputCsp { get; init; }
    public string? D3d11CompositionSize { get; init; }
    public string? D3d11Adapter { get; init; }
    public long? Wid { get; init; }
    public string TargetColorspaceHint { get; init; } = "auto";
    public string TargetColorspaceHintMode { get; init; } = "target";
    public bool TargetColorspaceHintStrict { get; init; } = true;
    public string? TargetPeak { get; init; }
    public string? TargetPrim { get; init; }
    public string? TargetTrc { get; init; }
    public string? TargetGamut { get; init; }

    public IReadOnlyDictionary<string, string> ToProperties()
    {
        Dictionary<string, string> properties = new(StringComparer.Ordinal)
        {
            ["gpu-context"] = GpuContext,
            ["target-colorspace-hint"] = TargetColorspaceHint,
            ["target-colorspace-hint-mode"] = TargetColorspaceHintMode,
            ["target-colorspace-hint-strict"] = TargetColorspaceHintStrict ? "yes" : "no",
        };

        AddIfSet(properties, "d3d11-output-mode", D3d11OutputMode);
        AddIfSet(properties, "d3d11-output-format", D3d11OutputFormat);
        AddIfSet(properties, "d3d11-output-csp", D3d11OutputCsp);
        AddIfSet(properties, "d3d11-composition-size", D3d11CompositionSize);
        AddIfSet(properties, "d3d11-adapter", D3d11Adapter);
        AddIfSet(properties, "target-peak", TargetPeak);
        AddIfSet(properties, "target-prim", TargetPrim);
        AddIfSet(properties, "target-trc", TargetTrc);
        AddIfSet(properties, "target-gamut", TargetGamut);
        if (Wid is { } wid)
        {
            // mpv wid is uint32 zero-extended into int64; never pass a negative value.
            properties["wid"] = unchecked((ulong)wid).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return properties;
    }

    private static void AddIfSet(IDictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }
}

public sealed record PlaybackPolicyOptions
{
    /// <summary>Night-mode graph. Not attached unless <see cref="NightMode"/> is on.</summary>
    public const string NightModeFilterGraph = "acompressor,dynaudnorm,alimiter";

    public string ToneMapping { get; init; } = "auto";
    public string HdrComputePeak { get; init; } = "auto";
    public string GamutMappingMode { get; init; } = "perceptual";
    public string Ao { get; init; } = "wasapi";
    public string AudioChannels { get; init; } = "auto-safe";
    public bool AudioExclusive { get; init; }
    /// <summary>Null leaves the previous value. Empty string clears passthrough.</summary>
    public string? AudioSpdif { get; init; }
    public string? AudioDevice { get; init; }
    public string? AudioDelay { get; init; }
    /// <summary>Null leaves the previous chain. Empty string clears <c>af</c>.</summary>
    public string? Af { get; init; }
    /// <summary>Passthrough forces this off.</summary>
    public bool NightMode { get; init; }
    public string SubFontProvider { get; init; } = "auto";
    public string? SubFontsDir { get; init; }
    public string SubAssOverride { get; init; } = "no";
    public string SubAuto { get; init; } = "fuzzy";
    public string SubFilePaths { get; init; } = "字幕;Subs;subs;subtitles";
    public string? SubHdrPeak { get; init; }
    public string? ImageSubsHdrPeak { get; init; }

    /// <summary>
    /// Three PCM strategies; only the channel layout differs. Passthrough
    /// (<see cref="WithPassthrough"/>) and night mode are separate toggles, so
    /// the three calls can be chained in any order.
    /// </summary>
    public PlaybackPolicyOptions WithAudioPolicy(Penrose.Core.Playback.AudioPolicy policy) =>
        policy switch
        {
            Penrose.Core.Playback.AudioPolicy.ForceStereo => this with { AudioChannels = "stereo" },
            Penrose.Core.Playback.AudioPolicy.HomeTheaterPcm => this with { AudioChannels = "7.1,5.1,stereo" },
            _ => this with { AudioChannels = "auto-safe" },
        };

    /// <summary>
    /// On: codecs in <see cref="Penrose.Core.Playback.AudioPassthrough.SpdifCodecs"/> go to the
    /// receiver undecoded over exclusive WASAPI; everything else is still decoded
    /// with the policy's layout. Night mode is cleared because lavfi filters
    /// cannot take spdif frames. Off: shared mode, no passthrough.
    /// </summary>
    public PlaybackPolicyOptions WithPassthrough(bool enabled) =>
        enabled
            ? WithNightMode(false) with
            {
                AudioExclusive = true,
                AudioSpdif = Penrose.Core.Playback.AudioPassthrough.SpdifCodecs,
            }
            : this with
            {
                AudioExclusive = false,
                AudioSpdif = "",
            };

    public PlaybackPolicyOptions WithNightMode(bool enabled) =>
        this with
        {
            NightMode = enabled,
            Af = enabled ? NightModeFilterGraph : "",
        };

    public IReadOnlyDictionary<string, string> ToProperties()
    {
        Dictionary<string, string> properties = new(StringComparer.Ordinal)
        {
            ["tone-mapping"] = ToneMapping,
            ["hdr-compute-peak"] = HdrComputePeak,
            ["gamut-mapping-mode"] = GamutMappingMode,
            ["ao"] = Ao,
            ["audio-channels"] = AudioChannels,
            ["audio-exclusive"] = AudioExclusive ? "yes" : "no",
            ["sub-font-provider"] = SubFontProvider,
            ["sub-ass-override"] = SubAssOverride,
            ["sub-auto"] = SubAuto,
            ["sub-file-paths"] = SubFilePaths,
        };
        AddIfSet(properties, "audio-device", AudioDevice);
        if (AudioSpdif is not null)
        {
            properties["audio-spdif"] = AudioSpdif;
        }

        AddIfSet(properties, "audio-delay", AudioDelay);
        if (Af is not null)
        {
            properties["af"] = Af;
        }

        AddIfSet(properties, "sub-fonts-dir", SubFontsDir);
        AddIfSet(properties, "sub-hdr-peak", SubHdrPeak);
        AddIfSet(properties, "image-subs-hdr-peak", ImageSubsHdrPeak);
        return properties;
    }

    private static void AddIfSet(IDictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }
}

public sealed record UserAdvancedOptions
{
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyList<OptionValidationResult> ValidateAll() =>
        Values.Keys.Select(key => OptionWhitelist.Validate(OptionLayer.UserAdvanced, key)).ToArray();
}
