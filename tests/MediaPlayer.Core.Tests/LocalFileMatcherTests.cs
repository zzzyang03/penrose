using MediaPlayer.Core.Capabilities;
using MediaPlayer.Core.Playback;
using MediaPlayer.Core.Sources;

namespace MediaPlayer.Core.Tests;

public sealed class LocalFileMatcherTests
{
    [Fact]
    public void Exact_stem_and_contained_name_rank_above_unrelated()
    {
        IReadOnlyList<string> ranked = LocalFileMatcher.Rank(
            "Show.S01E02.mkv",
            [
                new LocalFileMatcher.MatchCandidate("other.srt", @"C:\lib\other.srt"),
                new LocalFileMatcher.MatchCandidate("Show.S01E02.chs.ass", @"C:\lib\Show.S01E02.chs.ass"),
                new LocalFileMatcher.MatchCandidate("random.srt", @"C:\lib\random.srt"),
            ]);
        Assert.Equal(@"C:\lib\Show.S01E02.chs.ass", ranked[0]);
        Assert.DoesNotContain(@"C:\lib\other.srt", ranked);
    }

    [Fact]
    public void Priority_token_wins_among_same_episode()
    {
        IReadOnlyList<string> ranked = LocalFileMatcher.Rank(
            "Movie.2020.mkv",
            [
                new LocalFileMatcher.MatchCandidate("Movie.2020.eng.srt", @"D:\Movie.2020.eng.srt"),
                new LocalFileMatcher.MatchCandidate("Movie.2020.chs.srt", @"D:\Movie.2020.chs.srt"),
            ]);
        Assert.Equal(@"D:\Movie.2020.chs.srt", ranked[0]);
    }

    [Fact]
    public void Episode_token_ignores_1080p()
    {
        Assert.Equal("s01e02", LocalFileMatcher.EpisodeToken("Show.S01E02.1080p"));
        Assert.Null(LocalFileMatcher.EpisodeToken("Clip.1080p"));
    }

    [Fact]
    public void Extra_matcher_skips_same_dir_fuzzy_hits()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mp-match-" + Guid.NewGuid().ToString("N"));
        string subDir = Path.Combine(dir, "字幕");
        Directory.CreateDirectory(subDir);
        string video = Path.Combine(dir, "Film.mkv");
        File.WriteAllBytes(video, [0]);
        File.WriteAllText(Path.Combine(dir, "Film.srt"), "1");
        File.WriteAllText(Path.Combine(subDir, "Film.chs.ass"), "Dialogue:");
        try
        {
            IReadOnlyList<string> extra = LocalFileMatcher.MatchExtraSubtitles(video);
            Assert.Contains(Path.Combine(subDir, "Film.chs.ass"), extra);
            Assert.DoesNotContain(Path.Combine(dir, "Film.srt"), extra);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class ChapterListParserTests
{
    [Fact]
    public void Parses_title_and_time()
    {
        IReadOnlyList<ChapterInfo> chapters = ChapterListParser.Parse(
            """[{"title":"OP","time":12.5},{"title":"A","time":90}]""");
        Assert.Equal(2, chapters.Count);
        Assert.Equal("OP", chapters[0].Title);
        Assert.Equal(TimeSpan.FromSeconds(12.5), chapters[0].Start);
        Assert.Equal(1, chapters[1].Index);
    }

    [Fact]
    public void Empty_or_junk_is_empty_list()
    {
        Assert.Empty(ChapterListParser.Parse(null));
        Assert.Empty(ChapterListParser.Parse("not-json"));
        Assert.Empty(ChapterListParser.Parse("{}"));
    }
}

public sealed class HwdecCodecPolicyTests
{
    [Fact]
    public void Drops_prores_and_ffv1()
    {
        string next = HwdecCodecPolicy.Intersect("h264,hevc,vp9,av1,prores,ffv1,vp8");
        Assert.Equal("h264,hevc,vp9,av1", next);
        Assert.DoesNotContain("prores", next, StringComparison.Ordinal);
        Assert.DoesNotContain("vp8", next, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_input_uses_safe_default()
    {
        Assert.Equal(HwdecCodecPolicy.D3d11vaSafe, HwdecCodecPolicy.Intersect(null));
    }
}
