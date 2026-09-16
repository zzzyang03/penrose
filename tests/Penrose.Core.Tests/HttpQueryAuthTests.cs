using Penrose.Core.Playback;

namespace Penrose.Core.Tests;

public sealed class HttpQueryAuthTests
{
    [Fact]
    public void Applies_emby_token_as_api_key_query()
    {
        Uri uri = HttpQueryAuth.Apply(
            new Uri("https://emby.example/Items/1/Subtitles/2/Stream.ass"),
            new Dictionary<string, string> { ["X-Emby-Token"] = "secret-token" });
        Assert.Contains("api_key=secret-token", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_mediabrowser_authorization_token()
    {
        string? token = HttpQueryAuth.TryGetToken(new Dictionary<string, string>
        {
            ["Authorization"] = "MediaBrowser Client=\"Penrose\", Token=\"abc-123\", Device=\"Test-PC\"",
        });
        Assert.Equal("abc-123", token);
    }

    [Fact]
    public void Leaves_file_uris_and_existing_api_key_alone()
    {
        Uri file = HttpQueryAuth.Apply(
            new Uri("file:///tmp/a.ass"),
            new Dictionary<string, string> { ["X-Emby-Token"] = "secret-token" });
        Assert.True(file.IsFile);

        Uri already = HttpQueryAuth.Apply(
            new Uri("https://emby.example/sub.ass?api_key=kept"),
            new Dictionary<string, string> { ["X-Emby-Token"] = "other" });
        Assert.Contains("api_key=kept", already.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("other", already.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void Readable_file_requires_an_existing_path()
    {
        string missing = Path.Combine(Path.GetTempPath(), "mp-missing-" + Guid.NewGuid().ToString("N") + ".ass");
        Assert.False(HttpQueryAuth.IsReadableFile(new Uri(Path.GetFullPath(missing))));
    }

    [Fact]
    public void Without_server_credentials_drops_tokens_and_keeps_other_headers()
    {
        IReadOnlyDictionary<string, string> kept = HttpQueryAuth.WithoutServerCredentials(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Emby-Token"] = "secret",
                ["Authorization"] = "MediaBrowser Token=secret",
                ["User-Agent"] = "Penrose",
            });
        Assert.False(kept.ContainsKey("X-Emby-Token"));
        Assert.False(kept.ContainsKey("Authorization"));
        Assert.Equal("Penrose", kept["User-Agent"]);
    }
}
