using Penrose.Core.Sources;

namespace Penrose.Core.Tests;

public sealed class StrmParserTests
{
    [Fact]
    public void First_http_line_wins_comments_skipped()
    {
        Uri? uri = StrmParser.TryParse("# note\nhttps://cdn.example/a.mkv\nhttps://ignored.example/b.mkv\n");
        Assert.Equal(new Uri("https://cdn.example/a.mkv"), uri);
    }

    [Fact]
    public void Unc_is_allowed()
    {
        Uri? uri = StrmParser.TryParse(@"\\nas\media\film.mkv");
        Assert.NotNull(uri);
    }

    [Fact]
    public void Ftp_and_oversized_are_rejected()
    {
        Assert.Null(StrmParser.TryParse("ftp://host/a.mkv"));
        Assert.Null(StrmParser.TryParse(new string('a', StrmParser.MaxBytes + 1)));
        Assert.Null(StrmParser.TryParse("javascript:alert(1)"));
    }

    [Fact]
    public async Task Memory_credential_store_round_trips()
    {
        MemoryCredentialStore store = new();
        await store.SaveAsync("emby:token", "secret-token");
        Assert.Equal("secret-token", await store.LoadAsync("emby:token"));
        await store.DeleteAsync("emby:token");
        Assert.Null(await store.LoadAsync("emby:token"));
    }
}
