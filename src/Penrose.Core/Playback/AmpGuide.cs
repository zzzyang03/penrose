namespace Penrose.Core.Playback;

public enum AudioSinkKind
{
    Auto,
    Speakers,
    Hdmi,
    Other,
}

/// <summary>What the wizard applies: a PCM layout plus whether to bitstream.</summary>
public readonly record struct AmpRecommendation(AudioPolicy Policy, bool Passthrough);

/// <summary>
/// <c>audio-device=auto</c> may pick HDMI before laptop speakers.
/// The wizard pins a WASAPI device and maps it to an <see cref="AmpRecommendation"/>.
/// </summary>
public static class AmpGuide
{
    public static IReadOnlyList<AudioDeviceInfo> ForOutputPicker(IReadOnlyList<AudioDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices
            .Where(device =>
                device.Name.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || device.Name.StartsWith("wasapi/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static AudioSinkKind Classify(AudioDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.Name.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return AudioSinkKind.Auto;
        }

        if (!device.Name.StartsWith("wasapi/", StringComparison.OrdinalIgnoreCase))
        {
            return AudioSinkKind.Other;
        }

        string text = device.Description + " " + device.Name;
        if (LooksHdmi(text))
        {
            return AudioSinkKind.Hdmi;
        }

        if (LooksSpeakers(text))
        {
            return AudioSinkKind.Speakers;
        }

        return AudioSinkKind.Other;
    }

    public static AudioDeviceInfo? Prefer(IReadOnlyList<AudioDeviceInfo> devices, AudioSinkKind kind)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (kind == AudioSinkKind.Auto)
        {
            return devices.FirstOrDefault(device =>
                device.Name.Equals("auto", StringComparison.OrdinalIgnoreCase));
        }

        return devices.FirstOrDefault(device => Classify(device) == kind)
            ?? devices.FirstOrDefault(device =>
                device.Name.Equals("auto", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Passthrough is only offered for HDMI; speakers and auto never bitstream.</summary>
    public static AmpRecommendation Recommend(AudioSinkKind kind, bool passthrough, bool forceStereo = false) =>
        kind switch
        {
            AudioSinkKind.Hdmi => new(AudioPolicy.HomeTheaterPcm, passthrough),
            AudioSinkKind.Speakers => new(forceStereo ? AudioPolicy.ForceStereo : AudioPolicy.SystemCompatible, false),
            _ => new(AudioPolicy.SystemCompatible, false),
        };

    private static bool LooksHdmi(string text) =>
        ContainsAny(
            text,
            "hdmi",
            "nvidia high definition audio",
            "amd high definition audio",
            "intel display",
            "intel(r) display",
            "display audio",
            "s/pdif",
            "spdif",
            "optical",
            "digital audio",
            "数字输出");

    private static bool LooksSpeakers(string text) =>
        ContainsAny(
            text,
            "realtek",
            "扬声器",
            "speakers",
            "headphone",
            "headset",
            "耳机",
            "analog");

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
