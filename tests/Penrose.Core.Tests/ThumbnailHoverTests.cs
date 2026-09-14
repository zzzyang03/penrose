using Penrose.Core.Playback;

namespace Penrose.Core.Tests;

public sealed class ThumbnailHoverTests
{
    [Fact]
    public void Maps_slider_x_to_time()
    {
        TimeSpan duration = TimeSpan.FromSeconds(100);
        Assert.Equal(TimeSpan.Zero, ThumbnailHover.TimeAt(0, 200, duration));
        Assert.Equal(50, ThumbnailHover.TimeAt(100, 200, duration)!.Value.TotalSeconds, 3);
        Assert.Equal(100, ThumbnailHover.TimeAt(200, 200, duration)!.Value.TotalSeconds, 3);
        Assert.Null(ThumbnailHover.TimeAt(10, 0, duration));
        Assert.Null(ThumbnailHover.TimeAt(10, 100, TimeSpan.Zero));
    }

    [Fact]
    public void Quantize_reduces_hover_churn()
    {
        Assert.Equal(1.5, ThumbnailHover.Quantize(TimeSpan.FromSeconds(1.4), 0.5).TotalSeconds, 3);
        Assert.Equal(0, ThumbnailHover.Quantize(TimeSpan.FromSeconds(-1), 0.5).TotalSeconds, 3);
    }
}
