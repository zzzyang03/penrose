namespace Penrose.Core.Ui;

/// <summary>
/// High contrast and Reduced Motion change chrome behavior.
/// Color mapping stays in the host; this is the policy both packs can test.
/// </summary>
public static class AccessibilityPolicy
{
    public const int DefaultTapGuardMs = 220;

    public static bool ShouldAutoHideChrome(bool reduceMotion, bool highContrast) =>
        !reduceMotion && !highContrast;

    public static int TapGuardMilliseconds(bool reduceMotion) =>
        reduceMotion ? 0 : DefaultTapGuardMs;
}
