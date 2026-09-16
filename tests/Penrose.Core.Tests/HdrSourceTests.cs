using Penrose.Core.Playback;

namespace Penrose.Core.Tests;

public sealed class HdrSourceTests
{
    [Theory]
    [InlineData("pq", true)]
    [InlineData("PQ", true)]
    [InlineData("hlg", true)]
    [InlineData("st2084", true)]
    [InlineData("srgb", false)]
    [InlineData("bt.1886", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Transfer_gamma_detects_hdr(string? gamma, bool hdr)
    {
        Assert.Equal(hdr, HdrSource.IsTransferHdr(gamma));
    }

    [Fact]
    public void Dolby_vision_profile_is_hdr_even_without_pq_gamma()
    {
        Assert.True(HdrSource.IsSourceHdr(gamma: null, dolbyVisionProfile: 5));
        Assert.True(HdrSource.IsSourceHdr("srgb", dolbyVisionProfile: 8));
        Assert.False(HdrSource.IsSourceHdr(gamma: null, dolbyVisionProfile: null));
        Assert.False(HdrSource.IsSourceHdr("srgb", dolbyVisionProfile: 0));
        Assert.True(HdrSource.IsSourceHdr("pq", dolbyVisionProfile: null));
    }
}
