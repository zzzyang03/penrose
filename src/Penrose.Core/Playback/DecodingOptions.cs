namespace Penrose.Core.Playback;

/// <summary>Maps the hardware-decoding toggle to mpv's <c>hwdec</c>.</summary>
public static class DecodingOptions
{
    /// <summary>
    /// mpv picks a hardware decoder from its safe list; the same value the engine boots
    /// with (<see cref="Penrose.Core.Options.EngineBootstrapOptions.Hwdec"/>).
    /// </summary>
    public const string Hardware = "auto";

    /// <summary>Decode on the CPU.</summary>
    public const string Software = "no";

    public static IReadOnlyDictionary<string, string> ToProperties(bool hardwareDecoding) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hwdec"] = hardwareDecoding ? Hardware : Software,
        };
}
