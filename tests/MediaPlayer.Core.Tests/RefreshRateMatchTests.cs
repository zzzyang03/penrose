using MediaPlayer.Core.Playback;

namespace MediaPlayer.Core.Tests;

public sealed class RefreshRateMatchTests
{
    [Fact]
    public void Film_on_60hz_prefers_24_when_present()
    {
        Assert.Equal(24, RefreshRateMatch.SnapSourceFps(23.976));
        Assert.Equal(24, RefreshRateMatch.Pick(23.976, 60, [24, 30, 60]));
        Assert.Null(RefreshRateMatch.Pick(23.976, 24, [24, 60]));
        Assert.Null(RefreshRateMatch.Pick(24, 48, [24, 48, 60]));
    }

    [Fact]
    public void Pal_and_ntsc_video()
    {
        Assert.Equal(50, RefreshRateMatch.Pick(25, 60, [50, 60]));
        Assert.Null(RefreshRateMatch.Pick(25, 50, [50, 60]));
        Assert.Equal(60, RefreshRateMatch.Pick(29.97, 48, [48, 60]));
        Assert.Null(RefreshRateMatch.Pick(59.94, 60, [60, 120]));
    }

    [Fact]
    public void Missing_mode_does_not_invent_a_rate()
    {
        Assert.Null(RefreshRateMatch.Pick(23.976, 60, [60]));
        Assert.Equal(48, RefreshRateMatch.Pick(24, 60, [48, 60]));
    }
}
