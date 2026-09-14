using MediaPlayer.Core.Playback;

namespace MediaPlayer.Core.Tests;

public sealed class WindowsHdrPolicyTests
{
    [Fact]
    public void Enables_only_when_source_is_hdr_and_windows_hdr_is_off()
    {
        Assert.True(WindowsHdrPolicy.ShouldEnable(
            settingOn: true, sourceHdr: true, advancedColorSupported: true, advancedColorEnabled: false));
        Assert.False(WindowsHdrPolicy.ShouldEnable(
            settingOn: true, sourceHdr: true, advancedColorSupported: true, advancedColorEnabled: true));
        Assert.False(WindowsHdrPolicy.ShouldEnable(
            settingOn: true, sourceHdr: false, advancedColorSupported: true, advancedColorEnabled: false));
        Assert.False(WindowsHdrPolicy.ShouldEnable(
            settingOn: false, sourceHdr: true, advancedColorSupported: true, advancedColorEnabled: false));
        Assert.False(WindowsHdrPolicy.ShouldEnable(
            settingOn: true, sourceHdr: true, advancedColorSupported: false, advancedColorEnabled: false));
    }
}
