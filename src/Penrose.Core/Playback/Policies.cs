namespace Penrose.Core.Playback;

/// <summary>
/// User-visible HDR output strategy. Actual swapchain format/csp is recorded on
/// <see cref="OutputParams"/> and must not be inferred from this policy alone.
/// </summary>
public enum HdrPolicy
{
    /// <summary>mode=target plus host-injected display capabilities (default).</summary>
    AutoTarget,
    /// <summary>Keep source HDR signaling (advanced).</summary>
    KeepSource,
    /// <summary>Experimental source-dynamic metadata.</summary>
    SourceDynamic,
    /// <summary>Force SDR target.</summary>
    ForceSdr,
    /// <summary>hint=no, debug only.</summary>
    HintOff,
}

/// <summary>Audio output strategy. Bitstream failure must fall back to PCM with a visible prompt.</summary>
public enum AudioPolicy
{
    SystemCompatible,
    ForceStereo,
    HomeTheaterPcm,
    Bitstream,
}
