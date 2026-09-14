using Penrose.Core.Options;
using Penrose.Core.Playback;
using Penrose.Core.Settings;

namespace Penrose.Core.Tests;

public sealed class AmpGuideTests
{
    private const string W1List = """
        [{"name":"auto","description":"Autoselect device"},{"name":"wasapi/{76bae192-b153-4ad9-94a3-78b36e12c5a0}","description":"32GX (NVIDIA High Definition Audio)"},{"name":"wasapi/{ae4aaa3d-db6e-4ac4-83f5-df8e2560bf24}","description":"扬声器 (Realtek(R) Audio)"},{"name":"openal","description":"Default (openal)"}]
        """;

    [Fact]
    public void Parses_mpv_audio_device_list()
    {
        IReadOnlyList<AudioDeviceInfo> devices = AudioDeviceListParser.Parse(W1List);
        Assert.Equal(4, devices.Count);
        Assert.Equal("auto", devices[0].Name);
        Assert.Contains("NVIDIA", devices[1].Description, StringComparison.Ordinal);
        Assert.Contains("Realtek", devices[2].Description, StringComparison.Ordinal);
        Assert.Empty(AudioDeviceListParser.Parse(null));
        Assert.Empty(AudioDeviceListParser.Parse("not-json"));
    }

    [Fact]
    public void Picker_hides_openal()
    {
        IReadOnlyList<AudioDeviceInfo> picker = AmpGuide.ForOutputPicker(AudioDeviceListParser.Parse(W1List));
        Assert.Equal(3, picker.Count);
        Assert.DoesNotContain(picker, device => device.Name.StartsWith("openal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Classifies_hdmi_before_speakers()
    {
        IReadOnlyList<AudioDeviceInfo> devices = AudioDeviceListParser.Parse(W1List);
        Assert.Equal(AudioSinkKind.Auto, AmpGuide.Classify(devices[0]));
        Assert.Equal(AudioSinkKind.Hdmi, AmpGuide.Classify(devices[1]));
        Assert.Equal(AudioSinkKind.Speakers, AmpGuide.Classify(devices[2]));
        Assert.Equal(AudioSinkKind.Other, AmpGuide.Classify(devices[3]));
        Assert.Equal(AudioSinkKind.Speakers, AmpGuide.Classify(
            new AudioDeviceInfo("wasapi/{x}", "Speakers (Realtek High Definition Audio)")));
    }

    [Fact]
    public void Prefers_hdmi_or_speakers_from_the_list()
    {
        IReadOnlyList<AudioDeviceInfo> devices = AmpGuide.ForOutputPicker(AudioDeviceListParser.Parse(W1List));
        Assert.Equal("wasapi/{76bae192-b153-4ad9-94a3-78b36e12c5a0}", AmpGuide.Prefer(devices, AudioSinkKind.Hdmi)?.Name);
        Assert.Equal("wasapi/{ae4aaa3d-db6e-4ac4-83f5-df8e2560bf24}", AmpGuide.Prefer(devices, AudioSinkKind.Speakers)?.Name);
        Assert.Equal("auto", AmpGuide.Prefer(devices, AudioSinkKind.Auto)?.Name);
    }

    [Fact]
    public void Recommends_bitstream_only_for_hdmi()
    {
        Assert.Equal(AudioPolicy.Bitstream, AmpGuide.RecommendPolicy(AudioSinkKind.Hdmi, passthrough: true));
        Assert.Equal(AudioPolicy.HomeTheaterPcm, AmpGuide.RecommendPolicy(AudioSinkKind.Hdmi, passthrough: false));
        Assert.Equal(AudioPolicy.SystemCompatible, AmpGuide.RecommendPolicy(AudioSinkKind.Speakers, passthrough: true));
        Assert.Equal(AudioPolicy.ForceStereo, AmpGuide.RecommendPolicy(AudioSinkKind.Speakers, passthrough: false, forceStereo: true));
        Assert.Equal(AudioPolicy.SystemCompatible, AmpGuide.RecommendPolicy(AudioSinkKind.Auto, passthrough: true));
    }

    [Fact]
    public void Policy_options_emit_pinned_device()
    {
        IReadOnlyDictionary<string, string> properties = new PlaybackPolicyOptions
        {
            AudioDevice = "wasapi/{76bae192-b153-4ad9-94a3-78b36e12c5a0}",
        }.WithAudioPolicy(AudioPolicy.Bitstream).ToProperties();
        Assert.Equal("wasapi/{76bae192-b153-4ad9-94a3-78b36e12c5a0}", properties["audio-device"]);
        Assert.Equal("yes", properties["audio-exclusive"]);
        Assert.True(OptionWhitelist.Validate(OptionLayer.PlaybackPolicy, "audio-device").Accepted);
    }

    [Fact]
    public void Settings_roundtrip_audio_device()
    {
        SimpleSettings loaded = SimpleSettingsSerializer.FromJson(
            SimpleSettingsSerializer.ToJson(new SimpleSettings
            {
                AudioDevice = "wasapi/{ae4aaa3d-db6e-4ac4-83f5-df8e2560bf24}",
                AudioPolicy = AudioPolicy.HomeTheaterPcm,
            }));
        Assert.Equal("wasapi/{ae4aaa3d-db6e-4ac4-83f5-df8e2560bf24}", loaded.AudioDevice);
        Assert.Equal(AudioPolicy.HomeTheaterPcm, loaded.AudioPolicy);
        Assert.Null(new SimpleSettings().AudioDevice);
    }
}
