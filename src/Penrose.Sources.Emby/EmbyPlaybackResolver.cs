using Penrose.Core.Capabilities;
using Penrose.Core.Playback;
using Penrose.Core.Sources;

namespace Penrose.Sources.Emby;

public sealed class EmbyPlaybackResolver : IPlaybackResolver
{
    private readonly EmbyClient _client;
    private readonly string _userId;
    private readonly bool _allowServerFilePaths;

    public EmbyPlaybackResolver(EmbyClient client, string userId, bool allowServerFilePaths = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        _userId = userId;
        _allowServerFilePaths = allowServerFilePaths;
    }

    public Task<PlaybackCandidate> ResolveAsync(
        string itemId,
        PlaybackCapabilitySnapshot capabilities,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(itemId, capabilities, mediaSourceId: null, cancellationToken);

    /// <param name="mediaSourceId">The version chosen on the item page, or null for the server's first good one.</param>
    public async Task<PlaybackCandidate> ResolveAsync(
        string itemId,
        PlaybackCapabilitySnapshot capabilities,
        string? mediaSourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(capabilities);
        DeviceProfile profile = DeviceProfileBuilder.Build(capabilities);
        using System.Text.Json.JsonDocument doc = await _client
            .GetPlaybackInfoAsync(_userId, itemId, profile, cancellationToken)
            .ConfigureAwait(false);
        PlaybackCandidate parsed = EmbyPlaybackInfoParser.Parse(
            doc.RootElement.GetRawText(),
            _client.BaseAddress,
            itemId,
            _allowServerFilePaths,
            mediaSourceId);
        return AttachToken(parsed);
    }

    public Task<PlaybackCandidate> RefreshAsync(
        PlaybackCandidate expired,
        PlaybackCapabilitySnapshot capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expired);
        if (string.IsNullOrWhiteSpace(expired.ItemId))
        {
            throw new InvalidOperationException("Cannot refresh PlaybackInfo without ItemId.");
        }

        return ResolveAsync(expired.ItemId, capabilities, cancellationToken);
    }

    /// <summary>
    /// The access token goes only to the Emby server itself. strm-backed items
    /// resolve to whatever host the strm points at (cloud drives, other servers);
    /// sending the token there would leak the account.
    /// </summary>
    private PlaybackCandidate AttachToken(PlaybackCandidate parsed)
    {
        if (parsed.Uri.IsFile)
        {
            return parsed;
        }

        if (!HttpQueryAuth.IsSameOrigin(parsed.Uri, _client.BaseAddress))
        {
            return parsed with { Headers = HttpQueryAuth.WithoutServerCredentials(parsed.Headers) };
        }

        if (string.IsNullOrEmpty(_client.AccessToken))
        {
            return parsed;
        }

        Dictionary<string, string> headers = new(parsed.Headers, StringComparer.OrdinalIgnoreCase)
        {
            ["X-Emby-Token"] = _client.AccessToken,
        };
        return parsed with { Headers = headers };
    }
}
