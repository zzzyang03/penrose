using MediaPlayer.Diagnostics;

namespace MediaPlayer.Diagnostics.Tests;

public sealed class LogRedactorTests
{
    [Theory]
    [InlineData("Authorization: Bearer secret-token")]
    [InlineData("Cookie: sid=abc")]
    [InlineData("token=abc123")]
    [InlineData("password=hunter2")]
    [InlineData(@"\\user@nas\share\movie.mkv")]
    [InlineData("https://user:pass@example.invalid/play")]
    [InlineData("X-Emby-Token: secret-token")]
    [InlineData("\"AccessToken\": \"secret-token\"")]
    public void Redacts_known_secrets(string input)
    {
        string redacted = LogRedactor.Redact(input);
        Assert.DoesNotContain("secret-token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sid=abc", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\user@", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pass", redacted, StringComparison.Ordinal);
        Assert.Contains(LogRedactor.Mask, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainsSensitive_detects_authorization()
    {
        Assert.True(LogRedactor.ContainsSensitive("Authorization: Bearer abc"));
        Assert.False(LogRedactor.ContainsSensitive("opened file.mkv"));
    }
}
