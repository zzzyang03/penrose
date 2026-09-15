using Penrose.Core.Playback;

namespace Penrose.Core.Tests;

public sealed class AudioDelayTests
{
    [Fact]
    public void Step_is_50_ms()
    {
        Assert.Equal(50, AudioDelay.StepMilliseconds);
    }

    [Fact]
    public void Repeated_steps_do_not_drift()
    {
        double delay = 0.0;
        for (int i = 0; i < 3; i++)
        {
            delay = AudioDelay.Nudge(delay, AudioDelay.Step);
        }

        Assert.Equal(0.15, delay);
        Assert.Equal("0.15", AudioDelay.ToProperty(delay));
    }

    [Fact]
    public void Steps_cross_zero_back_to_exact_zero()
    {
        double delay = AudioDelay.Nudge(0.05, -AudioDelay.Step);
        Assert.Equal(0.0, delay);
        Assert.Equal("0 ms", AudioDelay.FormatMilliseconds(delay));

        Assert.Equal(-0.05, AudioDelay.Nudge(delay, -AudioDelay.Step));
    }

    [Theory]
    [InlineData("0.000000", 0.0)]
    [InlineData("0.050000", 0.05)]
    [InlineData("-0.250000", -0.25)]
    [InlineData(null, 0.0)]
    [InlineData("", 0.0)]
    [InlineData("auto", 0.0)]
    [InlineData("nan", 0.0)]
    public void Parse_reads_mpv_values(string? value, double expected)
    {
        Assert.Equal(expected, AudioDelay.Parse(value));
    }

    [Theory]
    [InlineData(0.05, "0.05")]
    [InlineData(-0.1, "-0.1")]
    [InlineData(1.25, "1.25")]
    [InlineData(0.0, "0")]
    public void ToProperty_uses_invariant_short_form(double seconds, string expected)
    {
        Assert.Equal(expected, AudioDelay.ToProperty(seconds));
    }

    [Theory]
    [InlineData(0.05, "+50 ms")]
    [InlineData(-1.25, "-1250 ms")]
    [InlineData(0.0, "0 ms")]
    [InlineData(0.0499999, "+50 ms")]
    [InlineData(-0.0004, "0 ms")]
    public void FormatMilliseconds_is_signed(double seconds, string expected)
    {
        Assert.Equal(expected, AudioDelay.FormatMilliseconds(seconds));
    }
}
