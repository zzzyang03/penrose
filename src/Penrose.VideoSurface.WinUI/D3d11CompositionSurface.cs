using Penrose.Core.Engine;
using Penrose.Core.Options;
using Penrose.Core.Playback;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Penrose.VideoSurface.WinUI;

/// <summary>
/// Composition SwapChainPanel. Advanced Color desktop → scRGB, otherwise SDR;
/// window focus never changes the pipeline. Windowed PQ is not applied here.
/// </summary>
public sealed class D3d11CompositionSurface : IVideoSurface
{
    private readonly SwapChainPanel _panel;
    private readonly nint _hwnd;
    private readonly DispatcherQueue _dispatcher;
    private IPlaybackEngine? _engine;
    private nint _boundAddress;
    private string _boundSize = "";
    private bool _sdrOverride;
    private int _lockCount;
    private bool _leavingTopLevel;
    private bool _binding;
    private bool _disposed;
    private bool _wasReconfiguring;
    private DispatcherTimer? _fsWatch;
    private HostWindowMoveTracker? _moveTracker;
    private EventHandler<DisplayColorCapabilities>? _displayChanged;
    private EventHandler? _topLevelLeaveRequested;
    private EventHandler? _deviceLost;

    public D3d11CompositionSurface(SwapChainPanel panel, nint hwnd)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _hwnd = hwnd;
        // Captured on the UI thread; mpv's window thread must not touch the panel.
        _dispatcher = panel.DispatcherQueue;
    }

    public long SurfaceGeneration { get; private set; }

    public long BoundGeneration { get; private set; } = -1;

    public SurfaceCapabilities Capabilities { get; } = new(
        CanOverlayUi: true,
        SupportsWindowedHdr: true,
        SupportsFullscreenHdrMetadata: false,
        SupportsPictureInPicture: true,
        SupportsIndependentFlip: false,
        RequiresExternalColorSpaceManagement: true,
        HandlesInput: false);

    public DisplayColorCapabilities? CurrentDisplay { get; private set; }

    public double CurrentRefreshRateHz => DxgiDisplay.RefreshRateHz(_hwnd);

    public string? CurrentOutputFormat { get; private set; }

    public string? CurrentOutputColorSpace { get; private set; }

    /// <summary>Layout the bound swap chain was sized for (pixels and composition scale).</summary>
    public SurfaceLayout BoundLayout { get; private set; }

    /// <summary>Whether IDXGISwapChain2::SetMatrixTransform accepted the inverse composition scale on the last bind.</summary>
    public bool InverseScaleApplied { get; private set; }

    public event EventHandler<SurfaceGenerationChangedEventArgs>? SurfaceGenerationChanged;

    public event EventHandler<DisplayColorCapabilities>? DisplayChanged
    {
        add => _displayChanged += value;
        remove => _displayChanged -= value;
    }

    /// <summary>Raised when mpv's swap chain disappeared (device removed, VO torn down) while bound.</summary>
    public event EventHandler? DeviceLost
    {
        add => _deviceLost += value;
        remove => _deviceLost -= value;
    }

    public event EventHandler? TopLevelLeaveRequested
    {
        add => _topLevelLeaveRequested += value;
        remove => _topLevelLeaveRequested -= value;
    }

    public bool IsBusy => _lockCount > 0 || _binding;

    public bool IsTopLevel { get; private set; }

    public bool IsInteractiveMove { get; private set; }

    public bool HasMoveTracker => _moveTracker is { IsHooked: true };

    public nint VoWindowHandle { get; private set; }

    public void BeginInteractiveMove() => IsInteractiveMove = true;

    public async Task EndInteractiveMoveAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInteractiveMove || _disposed)
        {
            return;
        }

        IsInteractiveMove = false;
        await BindAsync(force: true, cancellationToken).ConfigureAwait(true);
        await RefreshDisplayTargetsAsync(cancellationToken).ConfigureAwait(true);
    }

    public Task InitializeAsync(IPlaybackEngine engine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        engine.SnapshotChanged -= OnEngineSnapshot;
        engine.SnapshotChanged += OnEngineSnapshot;
        PublishDisplay(ReadDisplay());
        _moveTracker ??= new HostWindowMoveTracker(_hwnd);
        _moveTracker.EnterSizeMove -= OnHostEnterSizeMove;
        _moveTracker.ExitSizeMove -= OnHostExitSizeMove;
        _moveTracker.EnterSizeMove += OnHostEnterSizeMove;
        _moveTracker.ExitSizeMove += OnHostExitSizeMove;
        return Task.CompletedTask;
    }

    public Task AttachAsync(CancellationToken cancellationToken = default) =>
        BindAsync(force: true, cancellationToken);

    public async Task DetachAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Invalidate any BindAsync still polling for the swap chain: after this
        // point the pointer it would apply may already have been freed by mpv.
        SurfaceGeneration++;
        Unbind();
        if (_engine is not null && !_disposed)
        {
            await _engine.DetachSurfaceAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    public Task ResizeAsync(SurfaceLayout layout, CancellationToken cancellationToken = default) =>
        SurfaceBindGate.ShouldSkipBind(IsInteractiveMove, force: false)
            ? Task.CompletedTask
            : BindAsync(force: false, cancellationToken);

    public Task RecreateAsync(SurfaceRecreateReason reason, CancellationToken cancellationToken = default) =>
        SurfaceBindGate.ShouldSkipBind(IsInteractiveMove, force: false)
            ? Task.CompletedTask
            : BindAsync(force: true, cancellationToken);

    public async Task EnterTopLevelAsync(bool windowsHdrOn, CancellationToken cancellationToken = default)
    {
        if (_engine is null || IsTopLevel || _disposed)
        {
            return;
        }

        _lockCount++;
        try
        {
            SurfaceGeneration++;
            Unbind();
            DisplayColorCapabilities? display = ReadDisplay();
            PublishDisplay(display);
            // PQ only when Windows HDR is really on; a WCG-only desktop cannot take it.
            bool hdr = display?.IsHdr ?? windowsHdrOn;
            try
            {
                Dictionary<string, string> pre = new(StringComparer.Ordinal)
                {
                    ["keepaspect-window"] = "no",
                    ["border"] = "no",
                    ["ontop"] = "yes",
                    ["force-window"] = "yes",
                    ["fullscreen"] = "no",
                };
                if (MpvWin32.TryGetMonitorRect(_hwnd, out int x, out int y, out int width, out int height))
                {
                    pre["geometry"] = MpvWin32.Geometry(x, y, width, height);
                    pre["d3d11-composition-size"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "x"
                        + height.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await _engine.ApplyPropertiesAsync(pre, cancellationToken).ConfigureAwait(true);
                await _engine.ApplyPropertiesAsync(TopLevelFullscreen.EnterProperties(hdr, display), cancellationToken)
                    .ConfigureAwait(true);

                bool windowed = await WaitUntilAsync(
                    async () =>
                        string.Equals(await _engine.GetPropertyStringAsync("d3d11-output-mode", cancellationToken).ConfigureAwait(true), "window", StringComparison.Ordinal)
                        && string.Equals(await _engine.GetPropertyStringAsync("vo-configured", cancellationToken).ConfigureAwait(true), "yes", StringComparison.Ordinal),
                    8000,
                    cancellationToken).ConfigureAwait(true);
                if (!windowed)
                {
                    throw new InvalidOperationException("Top-level full-screen VO did not become ready (d3d11-output-mode=window).");
                }

                nint hwnd = 0;
                await WaitUntilAsync(
                    () =>
                    {
                        hwnd = MpvWin32.FindVoWindow(_hwnd);
                        return Task.FromResult(hwnd != 0);
                    },
                    2500,
                    cancellationToken).ConfigureAwait(true);
                if (hwnd == 0)
                {
                    throw new InvalidOperationException("mpv full-screen window not found.");
                }

                MpvWin32.CoverMonitor(hwnd, _hwnd);
                await Task.Delay(80, cancellationToken).ConfigureAwait(true);
                MpvWin32.CoverMonitor(hwnd, _hwnd);
                MpvWin32.HookLeaveKeys(hwnd, RequestLeaveFromVo);
                StartFullscreenWatch();
                VoWindowHandle = hwnd;

                IsTopLevel = true;
                CurrentOutputFormat = await _engine.GetPropertyStringAsync("d3d11-output-format", cancellationToken)
                    .ConfigureAwait(true);
                CurrentOutputColorSpace = await _engine.GetPropertyStringAsync("d3d11-output-csp", cancellationToken)
                    .ConfigureAwait(true);
            }
            catch
            {
                StopFullscreenWatch();
                MpvWin32.UnhookLeaveKeys();
                IsTopLevel = false;
                VoWindowHandle = 0;
                try
                {
                    await _engine.ApplyPropertiesAsync(
                            TopLevelFullscreen.LeaveProperties(display?.IsAdvancedColor ?? windowsHdrOn, display),
                            cancellationToken)
                        .ConfigureAwait(true);
                    await BindAsync(force: true, cancellationToken).ConfigureAwait(true);
                }
                catch
                {
                    // Host falls back to WinUI fullscreen and rebinds there.
                }

                throw;
            }
        }
        finally
        {
            _lockCount--;
        }
    }

    public async Task LeaveTopLevelAsync(bool windowsHdrOn, CancellationToken cancellationToken = default)
    {
        if (_engine is null || !IsTopLevel)
        {
            return;
        }

        _leavingTopLevel = true;
        StopFullscreenWatch();
        MpvWin32.UnhookLeaveKeys();
        _lockCount++;
        try
        {
            DisplayColorCapabilities? display = ReadDisplay();
            PublishDisplay(display);
            bool advanced = display?.IsAdvancedColor ?? windowsHdrOn;
            await _engine.ApplyPropertiesAsync(
                    TopLevelFullscreen.LeaveProperties(advanced, display),
                    cancellationToken)
                .ConfigureAwait(true);
            IsTopLevel = false;
            VoWindowHandle = 0;
            // Must agree with the pipeline LeaveProperties just chose, or the next
            // RefreshOutputPipelineAsync early-returns on a stale flag.
            _sdrOverride = !advanced;
            SurfaceGeneration++;
            if (!_disposed)
            {
                await BindAsync(force: true, cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            _lockCount--;
            _leavingTopLevel = false;
        }
    }

    /// <summary>
    /// Re-reads the window's display and switches between scRGB and SDR only when
    /// its Advanced Color state changed. Safe to call on every activation: a
    /// focus change alone is a no-op, so the swap chain survives it.
    /// </summary>
    public async Task RefreshOutputPipelineAsync(CancellationToken cancellationToken = default)
    {
        if (_engine is null || IsTopLevel || IsBusy || _disposed || SurfaceBindGate.ShouldSkipDisplayRefresh(IsInteractiveMove))
        {
            return;
        }

        DisplayColorCapabilities? display = ReadDisplay();
        PublishDisplay(display);
        bool advanced = display?.IsAdvancedColor ?? WindowsAdvancedColor.AnyHdrEnabled();
        bool wantSdr = !advanced;
        if (wantSdr == _sdrOverride && _boundAddress != 0)
        {
            return;
        }

        _lockCount++;
        try
        {
            Unbind();
            string pipeline = OutputPipeline.Windowed(advanced);
            SurfaceBootstrapOptions options = OutputPipeline.Apply(
                new SurfaceBootstrapOptions { D3d11OutputMode = "composition" },
                pipeline,
                display);
            await _engine.ApplyPropertiesAsync(options.ToProperties(), cancellationToken).ConfigureAwait(true);
            _sdrOverride = wantSdr;
            SurfaceGeneration++;
            await BindAsync(force: true, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _lockCount--;
        }
    }

    public async Task RefreshDisplayTargetsAsync(CancellationToken cancellationToken = default)
    {
        if (_engine is null || IsBusy || _disposed || SurfaceBindGate.ShouldSkipDisplayRefresh(IsInteractiveMove))
        {
            return;
        }

        DisplayColorCapabilities? display = ReadDisplay();
        if (display is null)
        {
            return;
        }

        DisplayColorCapabilities? previous = CurrentDisplay;
        bool same =
            previous is not null
            && previous.AdapterLuid == display.AdapterLuid
            && previous.ActiveColorMode == display.ActiveColorMode
            && previous.SanitizedPeakNits() == display.SanitizedPeakNits();
        PublishDisplay(display);
        if (same)
        {
            return;
        }

        if (IsTopLevel)
        {
            bool hdrChanged = previous is null || previous.IsHdr != display.IsHdr;
            if (hdrChanged)
            {
                // UPDATE_VO change: mpv rebuilds its window; the watch re-covers and
                // re-hooks the new HWND.
                await _engine.ApplyPropertiesAsync(
                        TopLevelFullscreen.EnterProperties(display.IsHdr, display),
                        cancellationToken)
                    .ConfigureAwait(true);
                return;
            }

            await _engine.ApplyPropertiesAsync(
                    display.ToTargetOptions(display.IsHdr ? OutputPipeline.Hdr10 : OutputPipeline.Sdr),
                    cancellationToken)
                .ConfigureAwait(true);
            return;
        }

        bool modeChanged = previous is null || previous.IsAdvancedColor != display.IsAdvancedColor;
        if (modeChanged)
        {
            await RefreshOutputPipelineAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        string pipeline = _sdrOverride ? OutputPipeline.Sdr : OutputPipeline.ScRgb;
        await _engine.ApplyPropertiesAsync(display.ToTargetOptions(pipeline), cancellationToken)
            .ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopFullscreenWatch();
        MpvWin32.UnhookLeaveKeys();
        if (_engine is not null)
        {
            _engine.SnapshotChanged -= OnEngineSnapshot;
        }

        if (_moveTracker is not null)
        {
            _moveTracker.EnterSizeMove -= OnHostEnterSizeMove;
            _moveTracker.ExitSizeMove -= OnHostExitSizeMove;
            _moveTracker.Dispose();
            _moveTracker = null;
        }

        SurfaceGeneration++;
        Unbind();
        IPlaybackEngine? engine = _engine;
        _engine = null;
        if (engine is not null)
        {
            try
            {
                await engine.DetachSurfaceAsync().ConfigureAwait(true);
            }
            catch (ObjectDisposedException)
            {
                // Engine already gone.
            }
        }
    }

    private void OnHostEnterSizeMove(object? sender, EventArgs e) => BeginInteractiveMove();

    private async void OnHostExitSizeMove(object? sender, EventArgs e)
    {
        try
        {
            await EndInteractiveMoveAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Rebind will be retried by the next size/display event.
        }
    }

    /// <summary>
    /// mpv can rebuild its VO without the host asking (device removed after sleep,
    /// adapter change). vo-configured flips false→true; when that happens while we
    /// are not the ones reconfiguring, verify the bound pointer is still current.
    /// </summary>
    private void OnEngineSnapshot(object? sender, PlaybackSnapshot snapshot)
    {
        bool reconfiguring = snapshot.Activity == Activity.Reconfiguring;
        bool finished = _wasReconfiguring && !reconfiguring;
        _wasReconfiguring = reconfiguring;
        if (!finished || _disposed || _binding || IsTopLevel || _lockCount > 0 || IsInteractiveMove)
        {
            return;
        }

        _ = _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await VerifyBindingAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Best effort; the next host-driven bind repairs it.
            }
        });
    }

    private async Task VerifyBindingAsync()
    {
        if (_engine is null || _disposed || _binding || IsTopLevel || _lockCount > 0 || _boundAddress == 0)
        {
            return;
        }

        long? current = await _engine.GetPropertyInt64Async("display-swapchain").ConfigureAwait(true);
        nint address = current is > 0 ? (nint)current.Value : 0;
        if (address == _boundAddress)
        {
            return;
        }

        if (address == 0)
        {
            Unbind();
            _deviceLost?.Invoke(this, EventArgs.Empty);
            return;
        }

        await BindAsync(force: true, CancellationToken.None).ConfigureAwait(true);
    }

    private async Task BindAsync(bool force, CancellationToken cancellationToken)
    {
        if (_engine is null || IsTopLevel || _disposed || SurfaceBindGate.ShouldSkipBind(IsInteractiveMove, force))
        {
            return;
        }

        SurfaceLayout layout = ReadLayout();
        if (layout.IsEmpty)
        {
            return;
        }

        if (!force
            && _boundAddress != 0
            && string.Equals(_boundSize, layout.CompositionSize, StringComparison.Ordinal))
        {
            return;
        }

        long generation = ++SurfaceGeneration;
        Unbind();
        _binding = true;
        try
        {
            await _engine.ApplyPropertiesAsync(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["d3d11-composition-size"] = layout.CompositionSize,
                },
                cancellationToken).ConfigureAwait(true);

            nint address = await WaitForSwapChainAsync(cancellationToken).ConfigureAwait(true);
            // Detach/Dispose/another bind bumped the generation while we polled: the
            // address may belong to a VO that no longer exists.
            if (_disposed || _engine is null || !SwapChainOwnership.ShouldApply(generation, SurfaceGeneration))
            {
                return;
            }

            if (!SwapChainOwnership.IsLiveAddress(address))
            {
                return;
            }

            SwapChainBinder.SetSwapChain(_panel, address);
            InverseScaleApplied = SwapChainBinder.TrySetInverseScale(address, layout.CompositionScaleX, layout.CompositionScaleY);
            _boundAddress = address;
            BoundGeneration = generation;
            BoundLayout = layout;
            _boundSize = layout.CompositionSize;
            CurrentOutputFormat = await _engine.GetPropertyStringAsync("d3d11-output-format", cancellationToken)
                .ConfigureAwait(true);
            CurrentOutputColorSpace = await _engine.GetPropertyStringAsync("d3d11-output-csp", cancellationToken)
                .ConfigureAwait(true);
            SurfaceGenerationChanged?.Invoke(this, new SurfaceGenerationChangedEventArgs(generation, address));
        }
        finally
        {
            _binding = false;
        }
    }

    private void Unbind()
    {
        if (_boundAddress == 0)
        {
            return;
        }

        try
        {
            SwapChainBinder.SetSwapChain(_panel, 0);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Panel may already be torn down during window close.
        }

        _boundAddress = 0;
        BoundGeneration = -1;
        _boundSize = "";
    }

    private async Task<nint> WaitForSwapChainAsync(CancellationToken cancellationToken)
    {
        if (_engine is null)
        {
            return 0;
        }

        for (int i = 0; i < 40; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || _engine is null)
            {
                return 0;
            }

            long? value = await _engine.GetPropertyInt64Async("display-swapchain", cancellationToken)
                .ConfigureAwait(true);
            if (value is > 0)
            {
                return (nint)value.Value;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(true);
        }

        return 0;
    }

    private async Task<bool> WaitUntilAsync(Func<Task<bool>> predicate, int timeoutMs, CancellationToken cancellationToken)
    {
        int steps = Math.Max(1, timeoutMs / 50);
        for (int i = 0; i < steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await predicate().ConfigureAwait(true))
            {
                return true;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(true);
        }

        return false;
    }

    private void StartFullscreenWatch()
    {
        _fsWatch ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _fsWatch.Tick -= OnFullscreenWatch;
        _fsWatch.Tick += OnFullscreenWatch;
        _fsWatch.Start();
    }

    private void StopFullscreenWatch()
    {
        if (_fsWatch is null)
        {
            return;
        }

        _fsWatch.Stop();
        _fsWatch.Tick -= OnFullscreenWatch;
    }

    private void OnFullscreenWatch(object? sender, object e)
    {
        if (_engine is null || !IsTopLevel || _leavingTopLevel || _disposed)
        {
            return;
        }

        nint hwnd = VoWindowHandle;
        bool alive = hwnd != 0 && MpvWin32.CoversMonitor(hwnd, _hwnd);
        if (!alive)
        {
            nint found = MpvWin32.FindVoWindow(_hwnd);
            if (found != 0)
            {
                VoWindowHandle = found;
                MpvWin32.CoverMonitor(found, _hwnd);
                hwnd = found;
            }
        }

        // After an UPDATE_VO change (HDR flip while top-level) mpv creates a new
        // window; without re-hooking, Esc/F/T stop working and WM_CLOSE reaches mpv.
        if (hwnd != 0 && !MpvWin32.IsHooked(hwnd))
        {
            MpvWin32.HookLeaveKeys(hwnd, RequestLeaveFromVo);
        }
    }

    private void RequestLeaveFromVo()
    {
        if (!IsTopLevel || _leavingTopLevel)
        {
            return;
        }

        _ = _dispatcher.TryEnqueue(() =>
        {
            if (!IsTopLevel || _leavingTopLevel)
            {
                return;
            }

            _topLevelLeaveRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private DisplayColorCapabilities? ReadDisplay() => DxgiDisplay.ForWindow(_hwnd);

    private void PublishDisplay(DisplayColorCapabilities? display)
    {
        if (display is null)
        {
            return;
        }

        CurrentDisplay = display;
        _displayChanged?.Invoke(this, display);
    }

    public SurfaceLayout ReadLayout()
    {
        double scaleX = _panel.CompositionScaleX <= 0 ? 1 : _panel.CompositionScaleX;
        double scaleY = _panel.CompositionScaleY <= 0 ? 1 : _panel.CompositionScaleY;
        return new SurfaceLayout(
            _panel.ActualWidth,
            _panel.ActualHeight,
            scaleX,
            scaleY,
            scaleX);
    }
}
