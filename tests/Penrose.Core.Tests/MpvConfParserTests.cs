using Penrose.Core.Options;
using Penrose.Core.Playback;
using Penrose.Core.Sources;

namespace Penrose.Core.Tests;

public sealed class MpvConfParserTests
{
    [Fact]
    public void Accepts_user_advanced_and_rejects_vo()
    {
        MpvConfParseResult result = MpvConfParser.Parse("""
            # comment
            deband=yes
            scale=ewa_lanczos
            vo=gpu
            --sub-delay=0.2
            """);
        Assert.Equal("yes", result.Accepted["deband"]);
        Assert.Equal("ewa_lanczos", result.Accepted["scale"]);
        Assert.Equal("0.2", result.Accepted["sub-delay"]);
        Assert.Contains(result.Rejected, item => item.Key == "vo" && !item.Accepted);
    }

    [Fact]
    public void Empty_is_ok()
    {
        MpvConfParseResult result = MpvConfParser.Parse("  \n# only comments\n");
        Assert.Empty(result.Accepted);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Unknown_and_structural_keys_are_listed()
    {
        MpvConfParseResult result = MpvConfParser.Parse("not-a-real-key=1\nvo=gpu\nsub-ass-override=scale");
        Assert.Equal("scale", result.Accepted["sub-ass-override"]);
        Assert.False(result.Accepted.ContainsKey("vo"));
        Assert.False(result.Accepted.ContainsKey("not-a-real-key"));
        Assert.Contains(result.Rejected, item => item.Key == "vo");
        Assert.Contains(result.Rejected, item => item.Key == "not-a-real-key");
    }
}

public sealed class DiscTitleParserTests
{
    [Fact]
    public void Count_becomes_numbered_titles()
    {
        IReadOnlyList<DiscTitleInfo> titles = DiscTitleParser.Parse("4");
        Assert.Equal(4, titles.Count);
        Assert.Equal(1, titles[0].Id);
        Assert.Equal(4, titles[3].Id);
    }

    [Fact]
    public void Json_objects_keep_labels()
    {
        IReadOnlyList<DiscTitleInfo> titles = DiscTitleParser.Parse(
            """[{"id":1,"title":"Main","length":3600},{"id":3,"name":"Bonus"}]""");
        Assert.Equal(2, titles.Count);
        Assert.Contains("Main", titles[0].Label, StringComparison.Ordinal);
        Assert.Equal(3, titles[1].Id);
    }

    [Fact]
    public void Comma_list_becomes_numbered_titles()
    {
        IReadOnlyList<DiscTitleInfo> titles = DiscTitleParser.Parse("1, 3, 4");
        Assert.Equal(3, titles.Count);
        Assert.Equal(3, titles[1].Id);
        Assert.Empty(DiscTitleParser.Parse("0"));
        Assert.Empty(DiscTitleParser.Parse(null));
    }
}

public sealed class DiscRootTests
{
    [Fact]
    public void Iso_extension_is_a_disc()
    {
        Assert.True(LocalPlaybackFactory.IsIsoPath(@"D:\movie.ISO"));
        Assert.False(LocalPlaybackFactory.IsIsoPath(@"D:\movie.mkv"));
    }

    [Fact]
    public void Bdmv_and_video_ts_resolve_to_the_disc_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "mp-disc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "BDMV"));
        try
        {
            Assert.Equal(Path.GetFullPath(root), LocalPlaybackFactory.ResolveDiscRoot(root));
            Assert.Equal(Path.GetFullPath(root), LocalPlaybackFactory.ResolveDiscRoot(Path.Combine(root, "BDMV")));
            PlaybackRequest request = LocalPlaybackFactory.FromUserInput(root);
            Assert.True(request.Uri.IsFile);
            Assert.Null(request.DiscTitle);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
