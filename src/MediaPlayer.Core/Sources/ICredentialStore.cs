namespace MediaPlayer.Core.Sources;

public interface ICredentialStore
{
    Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default);

    Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class MemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        _secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        _secrets.TryGetValue(key, out string? secret);
        return Task.FromResult(secret);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        _secrets.Remove(key);
        return Task.CompletedTask;
    }
}
