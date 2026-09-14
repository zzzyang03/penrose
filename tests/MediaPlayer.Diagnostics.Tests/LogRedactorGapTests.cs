using MediaPlayer.Diagnostics;

namespace MediaPlayer.Diagnostics.Tests;

public sealed class LogRedactorGapTests
{
    [Fact]
    public void Query_api_key_is_masked_but_url_survives()
    {
        string redacted = LogRedactor.Redact(
            "GET https://emby.example/videos/1/stream.mkv?MediaSourceId=abc&api_key=s3cr3t&Static=true failed");
        Assert.DoesNotContain("s3cr3t", redacted, StringComparison.Ordinal);
        Assert.Contains("&Static=true", redacted, StringComparison.Ordinal);
        Assert.Contains("MediaSourceId=abc", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"Token\":\"abc123\"}")]
    [InlineData("{\"ApiKey\": \"abc123\"}")]
    [InlineData("{\"Password\":\"abc123\",\"Username\":\"u\"}")]
    [InlineData("{\"Pw\":\"abc123\"}")]
    public void Json_quoted_secret_keys_are_masked(string json)
    {
        string redacted = LogRedactor.Redact(json);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.True(LogRedactor.ContainsSensitive(json));
    }

    [Fact]
    public void MediaBrowser_authorization_token_is_masked()
    {
        string header = "X-Emby-Authorization: MediaBrowser Client=\"MP\", Device=\"W\", Token=\"deadbeef\"";
        Assert.DoesNotContain("deadbeef", LogRedactor.Redact(header), StringComparison.Ordinal);

        // The same value without a header prefix (e.g. inside a JSON dump).
        string bare = "auth=MediaBrowser Client=\"MP\", Token=\"deadbeef\", Version=\"1\"";
        string redacted = LogRedactor.Redact(bare);
        Assert.DoesNotContain("deadbeef", redacted, StringComparison.Ordinal);
        Assert.Contains("Version=\"1\"", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaBrowser_token_header_variant_is_masked()
    {
        string redacted = LogRedactor.Redact("X-MediaBrowser-Token: abcdef");
        Assert.DoesNotContain("abcdef", redacted, StringComparison.Ordinal);
    }
}
