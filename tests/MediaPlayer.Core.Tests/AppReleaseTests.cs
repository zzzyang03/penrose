using MediaPlayer.Core.Release;

namespace MediaPlayer.Core.Tests;

public sealed class AppReleaseTests
{
    [Fact]
    public void Parses_semver_and_v_prefix()
    {
        Assert.True(AppRelease.TryParse("0.1.0", out Version a));
        Assert.Equal(new Version(0, 1, 0), a);
        Assert.True(AppRelease.TryParse("v0.2.0+git", out Version b));
        Assert.Equal(new Version(0, 2, 0), b);
        Assert.False(AppRelease.TryParse(" ", out _));
    }

    [Fact]
    public void Compare_orders_releases()
    {
        Assert.True(AppRelease.Compare("0.1.0", "0.2.0") < 0);
        Assert.Equal(0, AppRelease.Compare("0.1.0", "v0.1.0"));
        Assert.True(AppRelease.Compare("0.2.0", "0.1.9") > 0);
    }

    [Fact]
    public void Feed_must_be_http_or_https()
    {
        Assert.Equal(UpdateStatus.Disabled, AppRelease.ForFeed("0.1.0", null).Status);
        Assert.Equal(UpdateStatus.Disabled, AppRelease.ForFeed("0.1.0", "  ").Status);
        Assert.Equal(UpdateStatus.InvalidFeed, AppRelease.ForFeed("0.1.0", "ftp://example/updates").Status);
        Assert.Equal(UpdateStatus.InvalidFeed, AppRelease.ForFeed("0.1.0", "not-a-url").Status);
        Assert.Equal(UpdateStatus.ReadyToQuery, AppRelease.ForFeed("0.1.0", "https://example.invalid/updates").Status);
    }

    [Fact]
    public void Remote_newer_is_available()
    {
        Assert.Equal(UpdateStatus.Available, AppRelease.CompareRemote("0.1.0", "0.2.0").Status);
        Assert.Equal(UpdateStatus.UpToDate, AppRelease.CompareRemote("0.2.0", "0.2.0").Status);
        Assert.Equal(UpdateStatus.UpToDate, AppRelease.CompareRemote("0.2.0", "0.1.0").Status);
    }
}
