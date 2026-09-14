namespace MediaPlayer.Core.Playback;

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
}
