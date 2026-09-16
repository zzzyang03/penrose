namespace Penrose.Core.Playback;

/// <summary>
/// When to turn Windows Advanced Color on for an HDR source.
/// Does not call Win32; the host restores whatever it enabled.
/// </summary>
public static class WindowsHdrPolicy
{
    public static bool ShouldEnable(
        bool settingOn,
        bool sourceHdr,
        bool advancedColorSupported,
        bool advancedColorEnabled) =>
        settingOn && sourceHdr && advancedColorSupported && !advancedColorEnabled;

    /// <summary>
    /// The windowed scRGB pipeline must run whenever the source is HDR and
    /// Advanced Color is already on, even if we did not toggle it this time
    /// (restore failed, or Windows HDR was left on from the previous file).
    /// </summary>
    public static bool ShouldRefreshPipeline(bool sourceHdr, bool advancedColorEnabled) =>
        sourceHdr && advancedColorEnabled;
}
