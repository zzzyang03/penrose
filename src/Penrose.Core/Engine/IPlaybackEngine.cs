using Penrose.Core.Playback;

namespace Penrose.Core.Engine;

/// <summary>
/// Playback engine contract. Stop / close / detach / dispose are distinct
/// and must not be collapsed. <c>mpv_terminate_destroy</c> is allowed only
/// inside <see cref="IAsyncDisposable.DisposeAsync"/>.
/// </summary>
public interface IPlaybackEngine : IAsyncDisposable
{
    PlaybackSnapshot Snapshot { get; }

    event EventHandler<PlaybackSnapshot>? SnapshotChanged;

    /// <summary>
    /// Applies engine bootstrap options (as mpv options, before initialize)
    /// then starts the event loop. Must be called once.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes on FILE_LOADED for the new generation, not when loadfile returns.
    /// Failure is END_FILE(reason=error) plus the error string.
    /// </summary>
    Task<PlaybackSnapshot> LoadAsync(PlaybackRequest request, CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task ResumeAsync(CancellationToken cancellationToken = default);

    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop current media; engine stays Ready (Initialized, MediaPhase=Empty).
    /// </summary>
    Task StopPlaybackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// StopPlaybackAsync plus release of external subtitle temps and session Stop.
    /// </summary>
    Task CloseMediaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unbind SwapChainPanel / HWND. Engine stays alive.
    /// </summary>
    Task DetachSurfaceAsync(CancellationToken cancellationToken = default);

    Task ApplyPropertiesAsync(
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default);

    Task<string?> GetPropertyStringAsync(string name, CancellationToken cancellationToken = default);

    Task<long?> GetPropertyInt64Async(string name, CancellationToken cancellationToken = default);

    Task ExecuteCommandAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default);
}
