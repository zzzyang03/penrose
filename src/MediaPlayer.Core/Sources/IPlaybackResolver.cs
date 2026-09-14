using MediaPlayer.Core.Capabilities;

namespace MediaPlayer.Core.Sources;

public interface IPlaybackResolver
{
    Task<PlaybackCandidate> ResolveAsync(
        string itemId,
        PlaybackCapabilitySnapshot capabilities,
        CancellationToken cancellationToken = default);

    Task<PlaybackCandidate> RefreshAsync(
        PlaybackCandidate expired,
        PlaybackCapabilitySnapshot capabilities,
        CancellationToken cancellationToken = default);
}
