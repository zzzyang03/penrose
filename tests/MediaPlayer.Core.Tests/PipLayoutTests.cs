using MediaPlayer.Core.Playback;

namespace MediaPlayer.Core.Tests;

public sealed class PipLayoutTests
{
    [Fact]
    public void Widescreen_fits_the_width_cap()
    {
        (int width, int height) = PipLayout.SizeFor(1920, 1080);
        Assert.Equal(400, width);
        Assert.Equal(225, height);
    }

    [Fact]
    public void Unknown_aspect_uses_sixteen_by_nine()
    {
        (int width, int height) = PipLayout.SizeFor(0, 0);
        Assert.Equal(400, width);
        Assert.Equal(225, height);
    }
}
