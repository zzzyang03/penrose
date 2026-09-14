using Penrose.Core.Engine;

namespace Penrose.VideoSurface;

public enum SurfaceRecreateReason
{
    SizeChanged,
    CompositionScaleChanged,
    DisplayChanged,
    HdrModeChanged,
    OutputFormatChanged,
    DeviceLost,
    SleepResume,
    HostRequested,
}

public sealed record SurfaceGenerationChangedEventArgs(long Generation, nint SwapChainAddress);

public interface IVideoSurface : IAsyncDisposable
{
    long SurfaceGeneration { get; }

    SurfaceCapabilities Capabilities { get; }

    DisplayColorCapabilities? CurrentDisplay { get; }

    string? CurrentOutputFormat { get; }

    string? CurrentOutputColorSpace { get; }

    event EventHandler<SurfaceGenerationChangedEventArgs>? SurfaceGenerationChanged;

    event EventHandler<DisplayColorCapabilities>? DisplayChanged;

    event EventHandler? DeviceLost;

    Task InitializeAsync(IPlaybackEngine engine, CancellationToken cancellationToken = default);

    Task AttachAsync(CancellationToken cancellationToken = default);

    Task DetachAsync(CancellationToken cancellationToken = default);

    Task ResizeAsync(SurfaceLayout layout, CancellationToken cancellationToken = default);

    Task RecreateAsync(SurfaceRecreateReason reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// display-swapchain is a borrowed address. mpv owns it. Wrappers must not
/// take ownership; SetSwapChain(null) runs before mpv destroys the VO.
/// </summary>
public static class SwapChainOwnership
{
    public static bool IsLiveAddress(nint address) => address != 0;

    public static bool ShouldApply(long eventGeneration, long currentGeneration) =>
        eventGeneration == currentGeneration;
}
