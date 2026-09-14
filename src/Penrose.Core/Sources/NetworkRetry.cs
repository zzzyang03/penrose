namespace Penrose.Core.Sources;

/// <summary>
/// Transient network failures (disconnect, timeout) retry then re-resolve.
/// 401/403 is not retried here — that is <see cref="IPlaybackResolver.RefreshAsync"/>.
/// </summary>
public static class NetworkRetry
{
    public static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or TimeoutException or IOException;

    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int attempts,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (attempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts));
        }

        Exception? last = null;
        for (int i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex) && i < attempts - 1)
            {
                last = ex;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new InvalidOperationException("Retry exhausted.");
    }
}
