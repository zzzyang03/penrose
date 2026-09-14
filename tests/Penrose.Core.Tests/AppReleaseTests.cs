using Penrose.Core.Release;

namespace Penrose.Core.Tests;

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
    public void Reads_tag_from_github_release_document()
    {
        const string json = """{"url":"https://api.github.com/repos/zzzyang03/penrose/releases/1","tag_name":"v0.2.0","name":"Penrose 0.2.0","draft":false}""";
        Assert.True(AppRelease.TryReadTag(json, out string tag));
        Assert.Equal("v0.2.0", tag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""{"tag_name":42}""")]
    [InlineData("""{"tag_name":" "}""")]
    public void Rejects_documents_without_a_tag(string? json)
    {
        Assert.False(AppRelease.TryReadTag(json, out string tag));
        Assert.Equal("", tag);
    }

    [Fact]
    public void Remote_newer_is_available()
    {
        Assert.Equal(UpdateStatus.Available, AppRelease.CompareRemote("0.1.0", "v0.2.0").Status);
        Assert.Equal(UpdateStatus.UpToDate, AppRelease.CompareRemote("0.2.0", "0.2.0").Status);
        Assert.Equal(UpdateStatus.UpToDate, AppRelease.CompareRemote("0.2.0", "0.1.0").Status);
        Assert.Equal(UpdateStatus.InvalidVersion, AppRelease.CompareRemote("0.2.0", "latest").Status);
    }
}
