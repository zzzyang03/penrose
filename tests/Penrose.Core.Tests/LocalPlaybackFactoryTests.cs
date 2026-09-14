using Penrose.Core.Playback;
using Penrose.Core.Sources;

namespace Penrose.Core.Tests;

public sealed class LocalPlaybackFactoryTests
{
    [Fact]
    public void File_path_becomes_absolute_uri()
    {
        string path = Path.Combine(Path.GetTempPath(), "mp-local-" + Guid.NewGuid().ToString("N") + ".mkv");
        File.WriteAllBytes(path, [0]);
        try
        {
            PlaybackRequest request = LocalPlaybackFactory.FromPath(path, TimeSpan.FromSeconds(12));
            Assert.True(request.Uri.IsFile);
            Assert.Equal(TimeSpan.FromSeconds(12), request.StartPosition);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Strm_uses_first_whitelisted_line()
    {
        string path = Path.Combine(Path.GetTempPath(), "mp-local-" + Guid.NewGuid().ToString("N") + ".strm");
        File.WriteAllText(path, "# skip\nhttps://cdn.example/a.mkv\n");
        try
        {
            PlaybackRequest request = LocalPlaybackFactory.FromPath(path);
            Assert.Equal(new Uri("https://cdn.example/a.mkv"), request.Uri);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Strm_rejects_ftp()
    {
        string path = Path.Combine(Path.GetTempPath(), "mp-local-" + Guid.NewGuid().ToString("N") + ".strm");
        File.WriteAllText(path, "ftp://host/a.mkv\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => LocalPlaybackFactory.FromPath(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Http_url_does_not_need_a_local_file()
    {
        PlaybackRequest request = LocalPlaybackFactory.FromUserInput("https://cdn.example/a.mkv");
        Assert.Equal(new Uri("https://cdn.example/a.mkv"), request.Uri);
    }

    [Fact]
    public void Subtitle_extensions_are_recognized()
    {
        Assert.True(LocalPlaybackFactory.IsSubtitlePath(@"C:\a.ass"));
        Assert.True(LocalPlaybackFactory.IsSubtitlePath("b.srt"));
        Assert.False(LocalPlaybackFactory.IsSubtitlePath("c.mkv"));
        Assert.True(LocalPlaybackFactory.IsMediaPath("c.mkv"));
        Assert.False(LocalPlaybackFactory.IsMediaPath("c.ass"));
    }

    [Fact]
    public void Folder_lists_media_sorted_and_skips_subs()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mp-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "b.mkv"), [0]);
            File.WriteAllBytes(Path.Combine(dir, "a.mp4"), [0]);
            File.WriteAllText(Path.Combine(dir, "a.srt"), "1");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "x");
            IReadOnlyList<string> files = LocalPlaybackFactory.EnumerateMediaFiles(dir);
            Assert.Equal(2, files.Count);
            Assert.EndsWith("a.mp4", files[0], StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("b.mkv", files[1], StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, LocalPlaybackFactory.IndexOfPath(files, files[0]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Iso_file_becomes_a_disc_request()
    {
        string path = Path.Combine(Path.GetTempPath(), "mp-iso-" + Guid.NewGuid().ToString("N") + ".iso");
        File.WriteAllBytes(path, [0]);
        try
        {
            PlaybackRequest request = LocalPlaybackFactory.FromUserInput(path);
            Assert.True(request.Uri.IsFile);
            Assert.Null(request.DiscTitle);
            PlaybackRequest titled = LocalPlaybackFactory.FromDisc(path, discTitle: 2);
            Assert.Equal(2, titled.DiscTitle);
            Assert.False(titled.ToFileLocalOptions().ContainsKey("title"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
