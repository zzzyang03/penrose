using Penrose.Core.Options;
using Penrose.Core.Playback;

namespace Penrose.Core.Tests;

public sealed class DecodingOptionsTests
{
    [Fact]
    public void Hardware_decoding_uses_the_engine_bootstrap_value()
    {
        KeyValuePair<string, string> hwdec = Assert.Single(DecodingOptions.ToProperties(hardwareDecoding: true));
        Assert.Equal("hwdec", hwdec.Key);
        Assert.Equal("auto", hwdec.Value);
        Assert.Equal(new EngineBootstrapOptions().Hwdec, hwdec.Value);
    }

    [Fact]
    public void Software_decoding_turns_hwdec_off()
    {
        KeyValuePair<string, string> hwdec = Assert.Single(DecodingOptions.ToProperties(hardwareDecoding: false));
        Assert.Equal("hwdec", hwdec.Key);
        Assert.Equal("no", hwdec.Value);
    }
}
