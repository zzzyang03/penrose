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
}
