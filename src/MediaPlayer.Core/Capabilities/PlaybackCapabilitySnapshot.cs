namespace MediaPlayer.Core.Capabilities;

/// <summary>
/// Runtime capabilities used to build a DeviceProfile. Never a static table.
/// Generated before each IPlaybackResolver.Resolve call.
/// </summary>
public sealed record PlaybackCapabilitySnapshot
{
    public IReadOnlyList<string> DecoderNames { get; init; } = [];
    public IReadOnlyList<string> HwdecMethods { get; init; } = [];
    public bool DisplayIsHdr { get; init; }
    public string? HdrOutputPipeline { get; init; }
    public string? AudioDevice { get; init; }
    public string? AudioLayout { get; init; }
    public bool BitstreamAllowed { get; init; }
    /// <summary>
    /// Codecs the current audio device can sink as WASAPI exclusive IEC61937.
    /// Reference host HDMI: ac3, eac3, truehd. Never dts there.
    /// </summary>
    public IReadOnlyList<string> BitstreamCodecs { get; init; } = [];
    public IReadOnlyList<string> LocalSubtitleFormats { get; init; } = ["ass", "ssa", "srt", "vtt", "pgs", "vobsub"];
    public bool SoftwareDecodingAllowed { get; init; } = true;
    public bool UncReachable { get; init; }
    public long? GpuAdapterLuid { get; init; }
}
