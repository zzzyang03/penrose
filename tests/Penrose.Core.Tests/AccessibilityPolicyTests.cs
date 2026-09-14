using Penrose.Core.Ui;

namespace Penrose.Core.Tests;

public sealed class AccessibilityPolicyTests
{
    [Fact]
    public void Auto_hide_is_off_when_motion_is_reduced_or_contrast_is_high()
    {
        Assert.True(AccessibilityPolicy.ShouldAutoHideChrome(reduceMotion: false, highContrast: false));
        Assert.False(AccessibilityPolicy.ShouldAutoHideChrome(reduceMotion: true, highContrast: false));
        Assert.False(AccessibilityPolicy.ShouldAutoHideChrome(reduceMotion: false, highContrast: true));
        Assert.False(AccessibilityPolicy.ShouldAutoHideChrome(reduceMotion: true, highContrast: true));
    }

    [Fact]
    public void Reduced_motion_skips_the_double_tap_guard()
    {
        Assert.Equal(0, AccessibilityPolicy.TapGuardMilliseconds(reduceMotion: true));
        Assert.Equal(AccessibilityPolicy.DefaultTapGuardMs, AccessibilityPolicy.TapGuardMilliseconds(reduceMotion: false));
    }
}
