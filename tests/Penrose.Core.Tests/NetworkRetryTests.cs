using Penrose.Core.Sources;

namespace Penrose.Core.Tests;

public sealed class NetworkRetryTests
{
    [Fact]
    public async Task Succeeds_on_later_attempt()
    {
        int n = 0;
        int result = await NetworkRetry.RunAsync(
            _ =>
            {
                n++;
                if (n < 3)
                {
                    throw new HttpRequestException("down");
                }

                return Task.FromResult(7);
            },
            attempts: 3,
            delay: TimeSpan.Zero);

        Assert.Equal(7, result);
        Assert.Equal(3, n);
    }

    [Fact]
    public async Task Exhausted_rethrows_last_transient()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            NetworkRetry.RunAsync<int>(
                _ => throw new HttpRequestException("still down"),
                attempts: 2,
                delay: TimeSpan.Zero));
    }

    [Fact]
    public void Unauthorized_is_not_transient()
    {
        Assert.False(NetworkRetry.IsTransient(new InvalidOperationException("401")));
        Assert.True(NetworkRetry.IsTransient(new HttpRequestException("connection refused")));
    }
}
