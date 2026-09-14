using System.Security.Cryptography;
using System.Text;
using Penrose.Core.Sources;

namespace Penrose.App.WinUI;

/// <summary>Current-user DPAPI blobs under the app data directory. Not for passwords in settings JSON.</summary>
internal sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly string _directory;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Penrose.credentials");

    public DpapiCredentialStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] plain = Encoding.UTF8.GetBytes(secret);
        byte[] protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), protectedBytes);
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        string path = PathFor(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            byte[] protectedBytes = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
        }
        catch (CryptographicException)
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        string path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_directory, Convert.ToHexString(hash) + ".dpapi");
    }
}
