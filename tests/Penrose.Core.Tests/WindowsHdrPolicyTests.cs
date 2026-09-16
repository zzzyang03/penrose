using Penrose.Core.Playback;

namespace Penrose.Core.Tests;

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

    [Fact]
    public void Refresh_pipeline_when_source_is_hdr_and_windows_hdr_is_already_on()
    {
        Assert.True(WindowsHdrPolicy.ShouldRefreshPipeline(sourceHdr: true, advancedColorEnabled: true));
        Assert.False(WindowsHdrPolicy.ShouldRefreshPipeline(sourceHdr: true, advancedColorEnabled: false));
        Assert.False(WindowsHdrPolicy.ShouldRefreshPipeline(sourceHdr: false, advancedColorEnabled: true));
    }
}
