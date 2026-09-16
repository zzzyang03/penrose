using Penrose.Core.Capabilities;
using Penrose.Core.Input;
using Penrose.Core.Options;
using Penrose.Core.Playback;
using Penrose.Core.Release;
using Penrose.Core.Settings;
using Penrose.Core.Sources;
using Penrose.Core.Ui;
using Penrose.Sources.Emby;
using Penrose.Diagnostics;
using Penrose.Interop.LibMpv;
using Penrose.Persistence;
using Penrose.Playback.Mpv;
using Penrose.VideoSurface;
using Penrose.VideoSurface.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;
using WinRT;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Input;
using Serilog;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Gaming.Input;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Penrose.App.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _osdTimer;
    private readonly DispatcherTimer _padTimer;
    // Windows.UI.ViewManagement is a CoreWindow-era API. In an unpackaged WinUI 3
    // desktop app constructing it or subscribing to its change events can fail
    // with 0x80070490 (Element not found) depending on the OS build; none of it
    // may be allowed to take the window down, so everything is guarded.
    private readonly AccessibilitySettings? _access = TryCreateWinRt(() => new AccessibilitySettings());
    private readonly UISettings? _uiSettings = TryCreateWinRt(() => new UISettings());
    private bool _systemSettingsSubscribed;
    private readonly List<string> _playlist = [];
    private NativeMpvClient? _client;
    private MpvPlaybackEngine? _engine;
    private D3d11CompositionSurface? _surface;
    private PlaybackStore? _store;
    private IDiagnosticBundleExporter? _diagnostics;
    private SimpleSettings _settings = new();
        private Uri? _currentUri;
    /// <summary>
    /// What the user is actually playing, for the info overlay. Mirrors
    /// <c>_currentUri</c> in lifecycle: set when a load starts, reset on
    /// stop / return-home / return-to-library.
    /// </summary>
    private MediaSourceKind _currentSourceKind = MediaSourceKind.Unknown;
    /// <summary>
    /// 1 Hz refresh while the info overlay is visible. Stops when the
    /// overlay hides so we do not poll mpv at idle.
    /// </summary>
    private DispatcherTimer? _infoRefreshTimer;
    private bool _started;
    private bool _starting;
    private bool _seeking;
    private bool _night;
    private bool _muted;
    private bool _windowFocused = true;
    private bool _audioPolicyReady;
    private bool _suppressPlaylistAdvance;
    private bool _bitstreamFallback;
    /// <summary>The current track really leaves the AO as IEC61937 (spdif), not PCM.</summary>
    private bool _spdifActive;
    private bool _wantFullscreen;
    private bool _topLevelAutomatic;
    private bool _borderlessMaximize;
    private bool _pipMini;
    private bool _savedPipBounds;
    private PointInt32 _savedPipPosition;
    private SizeInt32 _savedPipSize;
    private int _playlistIndex = -1;
    private int _matchTicket;
    private long _endedGeneration = -1;
    private IReadOnlyList<ChapterInfo> _chapters = [];
    private AudioPolicy _audioPolicy = AudioPolicy.SystemCompatible;
    private int _focusEpoch;
    private int _resizeEpoch;
    private int _displayEpoch;
    private int _presenterEpoch;
    private AppWindow? _appWindow;
    private nint _hwnd;
    private bool _chromePinned;
    private readonly List<string> _pendingOpen = [];
    private double _speed = 1;
    private double _subDelay;
    private bool _doubleTapped;
    private bool _highContrast;
    private bool _reduceMotion;
    private DesktopAcrylicController? _acrylic;
    private SystemBackdropConfiguration? _acrylicConfig;
    private UiStrings _ui = UiStrings.Zh;
    private ICredentialStore _secrets = new MemoryCredentialStore();
    private EmbyClient? _emby;
    private EmbyPlaybackResolver? _embyResolver;
    private IContentProvider? _embyLibrary;
    /// <summary>Folder open on the Emby page; null is the user's libraries.</summary>
    private LibraryItem? _libraryFolder;
    /// <summary>Folders above the current one (innermost last), for Back and the breadcrumb.</summary>
    private readonly List<LibraryItem?> _libraryTrail = [];
    /// <summary>Server item now playing (null for local files) and the listing it was started from.</summary>
    private LibraryItem? _currentLibraryItem;
    private IReadOnlyList<LibraryItem> _libraryQueue = [];
    /// <summary>Next episode resolved and warmed ahead of the end of the current one.</summary>
    private (LibraryItem Item, PlaybackCandidate Candidate)? _prefetchedNext;
    private bool _prefetchStarted;
    private SessionReportCoordinator? _session;
    private ReportingContext? _reporting;
    /// <summary>Refresh change to undo, keyed to the monitor it was applied on.</summary>
    private RefreshChange? _refreshChange;
    /// <summary>Display whose HDR we switched on, so the restore hits the same one.</summary>
    private WindowsAdvancedColor.AdvancedColorTarget? _hdrTarget;
    /// <summary>Resume key of the current item; stable across Emby play sessions.</summary>
    private string? _progressKey;
    private string? _lastSavedSettingsJson;
    private MpvThumbnailGrabber? _thumbs;
    private int _thumbTicket;
    private TimeSpan? _thumbAt;
    private PadFace _padWas;
    private bool _shutdownStarted;
    private bool _shutdownComplete;

    /// <summary>Seek stills cycle through this many files instead of growing without bound.</summary>
    private const int ThumbRingSize = 16;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppVersion.Name;
        EnsureAcrylicBackdrop();

        // 1 Hz refresh for the info overlay. Started by ToggleInfoOverlayAsync
        // when the overlay shows, stopped when it hides.
        _infoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _infoRefreshTimer.Tick += async (_, _) =>
        {
            if (InfoOverlay.Visibility == Visibility.Visible)
            {
                await RefreshStatusAsync().ConfigureAwait(true);
            }
        };

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        if (_appWindow is not null)
        {
            string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(icon))
            {
                _appWindow.SetIcon(icon);
            }

            _appWindow.Changed += OnAppWindowChanged;
            // Window.Closed has no deferral: by the time it fires the window is
            // already going away, so anything after the first await can be lost.
            // AppWindow.Closing can be cancelled, which buys us the time to save
            // progress and shut mpv down before the window actually closes.
            _appWindow.Closing += OnAppWindowClosing;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDrag);
        ApplyCaptionColors();

        Root.DragOver += OnDragOver;
        Root.Drop += OnDrop;
        Root.PointerMoved += OnPointerMoved;
        Root.KeyDown += OnKeyDown;
        TopBar.PointerEntered += OnChromePointerEntered;
        TopBar.PointerExited += OnChromePointerExited;
        TopBar.GettingFocus += OnChromeGettingFocus;
        TopBar.LosingFocus += OnChromeLosingFocus;
        TransportBar.PointerEntered += OnChromePointerEntered;
        TransportBar.PointerExited += OnChromePointerExited;
        TransportBar.GettingFocus += OnChromeGettingFocus;
        TransportBar.LosingFocus += OnChromeLosingFocus;
        Root.IsTabStop = true;
        Root.Loaded += (_, _) =>
        {
            ApplyCaptionColors();
            Root.Focus(FocusState.Programmatic);
        };
        VideoPanel.SizeChanged += OnSizeChanged;
        VideoPanel.CompositionScaleChanged += OnCompositionScaleChanged;
        Activated += OnActivated;
        Closed += OnClosed;

        PosterGrid.ItemClick += async (s, e) => await OnPosterClickAsync(s, e).ConfigureAwait(true);
        ResumeList.ItemClick += async (_, e) => await OnResumeClickAsync(e).ConfigureAwait(true);
        WireItemPage();
        LibrarySearchClip.SizeChanged += (_, _) => ClipLibrarySearch();
        LibrarySearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await SearchLibraryAsync(LibrarySearchBox.Text).ConfigureAwait(true);
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CollapseLibrarySearch();
            }
        };
        PrevButton.Click += async (_, _) => await PlayRelativeAsync(-1).ConfigureAwait(true);
        PlayButton.Click += async (_, _) => await TogglePauseAsync().ConfigureAwait(true);
        NextButton.Click += async (_, _) => await PlayRelativeAsync(1).ConfigureAwait(true);
        AudioButton.Click += async (_, _) => await ShowTrackFlyoutAsync(AudioButton, "audio").ConfigureAwait(true);
        SubButton.Click += async (_, _) => await ShowTrackFlyoutAsync(SubButton, "sub").ConfigureAwait(true);
        PlaylistButton.Click += (_, _) => TogglePlaylistPane();
        PlaylistView.ItemClick += async (_, e) => await OnPlaylistItemClickAsync(e).ConfigureAwait(true);
        PlaylistPane.PointerEntered += OnChromePointerEntered;
        PlaylistPane.PointerExited += OnChromePointerExited;
        PlaylistPane.GettingFocus += OnChromeGettingFocus;
        PlaylistPane.LosingFocus += OnChromeLosingFocus;
        ErrorPanel.GettingFocus += OnChromeGettingFocus;
        ErrorPanel.LosingFocus += OnChromeLosingFocus;
        InfoOverlay.PointerEntered += OnChromePointerEntered;
        InfoOverlay.PointerExited += OnChromePointerExited;
        SubscribeSystemSettings();
        VideoPanel.PointerWheelChanged += OnVideoWheel;
        VideoPanel.Tapped += OnVideoTapped;
        VideoPanel.DoubleTapped += OnVideoDoubleTapped;
        ChapterButton.Click += async (_, _) => await ShowChapterFlyoutAsync().ConfigureAwait(true);
        SettingsButton.Click += (_, _) => OpenSettings();
        InfoButton.Click += async (_, _) => await ToggleInfoOverlayAsync().ConfigureAwait(true);
        FullButton.Click += async (_, _) => await ToggleFullscreenAsync().ConfigureAwait(true);
        LeaveFullButton.Click += async (_, _) => await LeaveAnyFullscreenAsync().ConfigureAwait(true);
        VolumeButton.Click += async (_, _) => await ToggleMuteAsync().ConfigureAwait(true);
        EmptyOpenFile.Click += async (_, _) => await OpenFileAsync().ConfigureAwait(true);
        EmptyOpenFolder.Click += async (_, _) => await OpenFolderAsync().ConfigureAwait(true);
        EmptyOpenUrl.Click += async (_, _) => await OpenUrlAsync().ConfigureAwait(true);
        LibraryAccountButton.Click += (_, _) => ShowServersHome();
        LibraryConnectButton.Click += (_, _) => ShowServersHome();
        BackToLibraryButton.Click += async (_, _) => await ReturnFromPlaybackAsync().ConfigureAwait(true);
        SortOrderButton.Click += async (_, _) =>
        {
            _librarySort = _librarySort with { Descending = !_librarySort.Descending };
            UpdateSortUi();
            await RefreshEmbyPageAsync().ConfigureAwait(true);
        };
        PosterGrid.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
            {
                LayoutPosterGrid();
            }
        };
        WireHome();
        LibraryBackButton.Click += async (_, _) => await LibraryBackAsync().ConfigureAwait(true);
        RecentView.ItemClick += async (_, e) =>
        {
            if (e.ClickedItem is RecentRow recent)
            {
                await PlayPathAsync(recent.Path).ConfigureAwait(true);
            }
        };
        PlaylistCloseButton.Click += (_, _) => TogglePlaylistPane();
        ChannelsButton.Click += (_, _) => ShowChannelsFlyout();
        PositionSlider.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => _seeking = true),
            handledEventsToo: true);
        PositionSlider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(async (_, _) => await CommitSeekAsync().ConfigureAwait(true)),
            handledEventsToo: true);
        PositionSlider.PointerMoved += OnSeekHover;
        PositionSlider.PointerExited += (_, _) => HideThumbPreview();
        PositionSlider.PointerCaptureLost += async (_, _) => await CommitSeekAsync().ConfigureAwait(true);
        VolumeSlider.ValueChanged += async (_, e) => await SetVolumeAsync(e.NewValue).ConfigureAwait(true);

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _hideTimer.Tick += (_, _) => HideChrome();
        _osdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _osdTimer.Tick += (_, _) =>
        {
            _osdTimer.Stop();
            FadeOut(OsdPanel);
        };
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _progressTimer.Tick += async (_, _) =>
        {
            await SaveProgressAsync().ConfigureAwait(true);
            await SaveSettingsAsync().ConfigureAwait(true);
        };
        _progressTimer.Start();
        _padTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _padTimer.Tick += async (_, _) => await PollGamepadAsync().ConfigureAwait(true);
        _padTimer.Start();
        ApplyChrome();
        ApplyAccessibility();
    }

    public void OpenFromShell(string path)
    {
        BringToFront();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_engine is null || _starting)
        {
            _pendingOpen.Add(path);
            return;
        }

        _ = PlayPathAsync(path);
    }

    public void BringToFront()
    {
        if (_appWindow?.Presenter is OverlappedPresenter overlapped
            && overlapped.State == OverlappedPresenterState.Minimized)
        {
            overlapped.Restore();
        }

        _appWindow?.Show();
        Activate();
    }

    private void ApplyCaptionColors()
    {
        if (_appWindow?.TitleBar is null)
        {
            return;
        }

        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        Windows.UI.Color fg = _highContrast
            ? ReadSystemColor(UIColorType.Foreground, Colors.White)
            : Colors.White;
        _appWindow.TitleBar.ButtonForegroundColor = fg;
        _appWindow.TitleBar.ButtonInactiveForegroundColor = _highContrast
            ? fg
            : Colors.Gray;
        SyncCaptionBarButtons();
    }

    /// <summary>
    /// Settings / info use the same cell as min / max / close and sit flush
    /// against that strip so the five buttons share one pitch.
    /// </summary>
    private void SyncCaptionBarButtons()
    {
        if (_appWindow?.TitleBar is null)
        {
            return;
        }

        double inset = _appWindow.TitleBar.RightInset / TitleBarScale();
        double width = inset >= 90 ? inset / 3 : 46;
        SizeCaptionButton(SettingsButton, width, 48);
        SizeCaptionButton(InfoButton, width, 48);
        TopActions.Spacing = 0;
        TopActions.Margin = new Thickness(0, 0, Math.Max(0, inset), 0);
    }

    private static void SizeCaptionButton(Button button, double width, double height)
    {
        button.Width = width;
        button.MinWidth = width;
        button.Height = height;
        button.MinHeight = height;
    }

    private double TitleBarScale()
    {
        double fromXaml = Root.XamlRoot?.RasterizationScale ?? 0;
        if (fromXaml > 0)
        {
            return fromXaml;
        }

        if (_hwnd != 0)
        {
            uint dpi = GetDpiForWindow(_hwnd);
            if (dpi > 0)
            {
                return dpi / 96.0;
            }
        }

        return 1;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private void SetMediaTitle(string title)
    {
        Title = title;
        TitleText.Text = title;
    }

    private void ApplyLanguage()
    {
        _ui = UiStrings.For(_settings.Language);
        ApplyChrome();
    }

    private void ApplyChrome()
    {
        // Icon buttons: the label lives in the tooltip and the automation name.
        Label(InfoButton, _ui.Info);
        Label(SettingsButton, _ui.Settings);
        Label(PrevButton, _ui.Previous);
        Label(NextButton, _ui.Next);
        Label(AudioButton, _ui.AudioTracks);
        Label(SubButton, _ui.Subtitles);
        Label(ChapterButton, _ui.Chapters);
        Label(PlaylistButton, _ui.Playlist);
        Label(PlaylistCloseButton, _ui.ClosePane);
        Label(VolumeButton, _muted ? _ui.Unmute : _ui.Mute);
        Label(ChannelsButton, _ui.Channels);
        SetPlayIcon(_engine is not null && IsPlaying(_engine.Snapshot));
        SetFullscreenIcon(IsWinUiFullscreen() || _surface is { IsTopLevel: true } || IsPip());

        LeaveFullButton.Content = _ui.LeaveWindow;
        ErrorPanel.Title = _ui.CannotPlay;
        FallbackBanner.Message = _topLevelAutomatic ? _ui.FallbackHdrBanner : _ui.FallbackBanner;
        InfoHeader.Text = _ui.Info;
        LibraryHeader.Text = LibraryBreadcrumb();
        LibrarySearchBox.PlaceholderText = _ui.LibrarySearchPlaceholder;
        Label(LibrarySearchButton, _ui.Search);
        ResumeHeader.Text = _ui.ContinueWatching;
        LibraryHeaderAll.Text = _ui.AllLibraries;
        SetName(ResumeList, _ui.ContinueWatching);
        ToolTipService.SetToolTip(LibraryBackButton, _ui.Back);
        SetName(LibraryBackButton, _ui.Back);
        PlaylistHeader.Text = _ui.Playlist;
        EmptyOpenFileText.Text = _ui.OpenFile + "…";
        EmptyOpenFolderText.Text = _ui.OpenFolder + "…";
        EmptyOpenUrlText.Text = _ui.OpenUrl + "…";
        LibraryConnectText.Text = _ui.Connect;
        ToolTipService.SetToolTip(LibraryAccountButton, _ui.MediaServers);
        SetName(LibraryAccountButton, _ui.MediaServers);
        UpdateTransportVisibility();
        SetName(SortButton, _ui.SortLabel);
        ApplyItemPageLanguage();
        ApplyHomeLanguage();
        BuildSortMenu();
        if (_currentUri is null)
        {
            SetMediaTitle(AppVersion.Name);
        }

        SetName(LeaveFullButton, _ui.LeaveWindow);
        SetName(PosterGrid, _ui.Library);
        SetName(RecentView, _ui.RecentHeader);
        SetName(PositionSlider, _ui.Position);
        SetName(ThumbPreview, _ui.SeekThumbnails);
        SetName(VolumeSlider, _ui.Volume);
        SetName(VideoPanel, _ui.Video);
        SetName(PlaylistView, _ui.Playlist);
        SetName(InfoOverlay, _ui.Info);
        AutomationProperties.SetLiveSetting(OsdPanel, AutomationLiveSetting.Assertive);
        AutomationProperties.SetLiveSetting(ErrorPanel, AutomationLiveSetting.Assertive);
        AutomationProperties.SetLiveSetting(HintBanner, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(FallbackBanner, AutomationLiveSetting.Assertive);
        VideoPanel.IsTabStop = false;
    }

    /// <summary>Tooltip + accessible name for an icon-only button.</summary>
    private static void Label(Button button, string text)
    {
        ToolTipService.SetToolTip(button, text);
        AutomationProperties.SetName(button, text);
    }

    /// <summary>
    /// mpv reports pause=no while idle, so PlaybackIntent alone reads "playing" with
    /// nothing loaded; the play button must show Play until a file is actually up.
    /// </summary>
    private static bool IsPlaying(PlaybackSnapshot snapshot) =>
        snapshot.PlaybackIntent == PlaybackIntent.Playing
        && snapshot.MediaPhase is MediaPhase.Loaded or MediaPhase.Opening;

    private void SetPlayIcon(bool playing)
    {
        PlayIcon.Symbol = playing ? Symbol.Pause : Symbol.Play;
        Label(PlayButton, playing ? _ui.Pause : _ui.Play);
    }

    private void SetFullscreenIcon(bool fullscreen)
    {
        FullIcon.Symbol = fullscreen ? Symbol.BackToWindow : Symbol.FullScreen;
        Label(FullButton, fullscreen ? _ui.ExitFullscreen : _ui.Fullscreen);
    }

    private void SetVolumeIcon()
    {
        VolumeIcon.Symbol = _muted || VolumeSlider.Value <= 0 ? Symbol.Mute : Symbol.Volume;
        Label(VolumeButton, _muted ? _ui.Unmute : _ui.Mute);
    }

    private static void SetName(DependencyObject target, string name) =>
        AutomationProperties.SetName(target, name);

    private static T? TryCreateWinRt<T>(Func<T> factory)
        where T : class
    {
        try
        {
            return factory();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException)
        {
            Log.Warning(ex, "{Type} unavailable; using defaults", typeof(T).Name);
            return null;
        }
    }

    /// <summary>
    /// Subscribes to high-contrast / system-colour changes when the OS lets an
    /// unpackaged desktop app do so. If not, the state is re-read whenever the
    /// window is activated (see <see cref="OnActivated"/>), which covers a user
    /// toggling high contrast and coming back.
    /// </summary>
    private void SubscribeSystemSettings()
    {
        bool any = false;
        try
        {
            if (_access is not null)
            {
                _access.HighContrastChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyAccessibility);
                any = true;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Warning(ex, "AccessibilitySettings.HighContrastChanged unavailable; will re-read on activation");
        }

        try
        {
            if (_uiSettings is not null)
            {
                _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyAccessibility);
                any = true;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Warning(ex, "UISettings.ColorValuesChanged unavailable; will re-read on activation");
        }

        _systemSettingsSubscribed = any;
    }

    private bool ReadHighContrast()
    {
        try
        {
            return _access?.HighContrast ?? false;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return false;
        }
    }

    private bool ReadAnimationsEnabled()
    {
        try
        {
            return _uiSettings?.AnimationsEnabled ?? true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return true;
        }
    }

    private Windows.UI.Color ReadSystemColor(UIColorType type, Windows.UI.Color fallback)
    {
        try
        {
            return _uiSettings?.GetColorValue(type) ?? fallback;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return fallback;
        }
    }

    private void ApplyAccessibility()
    {
        _highContrast = ReadHighContrast();
        _reduceMotion = !ReadAnimationsEnabled();
        if (_highContrast)
        {
            Windows.UI.Color bg = ReadSystemColor(UIColorType.Background, Colors.Black);
            Windows.UI.Color fg = ReadSystemColor(UIColorType.Foreground, Colors.White);
            Windows.UI.Color accent = ReadSystemColor(UIColorType.Accent, Colors.DodgerBlue);
            // High contrast: opaque system colours everywhere, no scrims. InfoBars
            // follow the system theme on their own.
            SolidColorBrush background = new(bg);
            SolidColorBrush foreground = new(fg);
            SolidColorBrush highlight = new(accent);
            ReleaseAcrylicBackdrop();
            Root.Background = background;
            EmptyState.Background = background;
            HomeLeft.Background = background;
            SettingsPanel.Background = background;
            TopScrim.Background = background;
            BottomScrim.Background = background;
            PlaylistPane.Background = background;
            EmbyPage.Background = background;
            InfoOverlay.Background = background;
            OsdPanel.Background = background;
            ThumbPreview.Background = background;
            TitleText.Foreground = foreground;
            StatusText.Foreground = foreground;
            TimeText.Foreground = foreground;
            OsdText.Foreground = foreground;
            ThumbTime.Foreground = foreground;
            BrandTitle.Foreground = foreground;
            BrandVersion.Foreground = foreground;
            MiniProgress.Foreground = highlight;
        }
        else
        {
            TopScrim.Background = Brush("TopScrimBrush");
            BottomScrim.Background = Brush("BottomScrimBrush");
            PlaylistPane.Background = Brush("PanelBrush");
            InfoOverlay.Background = Brush("OverlayBrush");
            OsdPanel.Background = Brush("OsdBrush");
            ThumbPreview.Background = Brush("PanelBrush");
            TitleText.Foreground = Brush("TextPrimaryBrush");
            StatusText.Foreground = Brush("TextPrimaryBrush");
            TimeText.Foreground = Brush("TextSecondaryBrush");
            OsdText.Foreground = Brush("TextPrimaryBrush");
            ThumbTime.Foreground = Brush("TextSecondaryBrush");
            BrandTitle.Foreground = Brush("TextPrimaryBrush");
            BrandVersion.Foreground = Brush("TextSecondaryBrush");
            MiniProgress.ClearValue(Control.ForegroundProperty);
        }

        ApplyCaptionColors();
        UpdatePageBackdrop();
        if (!AccessibilityPolicy.ShouldAutoHideChrome(_reduceMotion, _highContrast))
        {
            ShowChrome();
            _hideTimer.Stop();
        }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private void OnChromeGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        _chromePinned = true;
        ShowChrome();
        _hideTimer.Stop();
    }

    private void OnChromeLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        if (IsUnderChrome(args.NewFocusedElement as DependencyObject))
        {
            return;
        }

        _chromePinned = false;
        RestartHideTimer();
    }

    private bool IsUnderChrome(DependencyObject? node)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, TopBar)
                || ReferenceEquals(node, TransportBar)
                || ReferenceEquals(node, PlaylistPane)
                || ReferenceEquals(node, EmbyPage)
                || ReferenceEquals(node, InfoOverlay)
                || ReferenceEquals(node, ErrorPanel))
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private void OnVideoWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!_audioPolicyReady)
        {
            return;
        }

        int delta = e.GetCurrentPoint(VideoPanel).Properties.MouseWheelDelta;
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (delta > 0 ? 5 : -5), 0, 100);
        e.Handled = true;
    }

    private async void OnVideoTapped(object sender, TappedRoutedEventArgs e)
    {
        int guard = AccessibilityPolicy.TapGuardMilliseconds(_reduceMotion);
        if (guard > 0)
        {
            await Task.Delay(guard).ConfigureAwait(true);
        }
        if (_doubleTapped)
        {
            _doubleTapped = false;
            return;
        }

        await TogglePauseAsync().ConfigureAwait(true);
    }

    private async void OnVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _doubleTapped = true;
        await ToggleFullscreenAsync().ConfigureAwait(true);
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        bool focused = e.WindowActivationState != WindowActivationState.Deactivated;
        _windowFocused = focused;
        if (_acrylicConfig is not null)
        {
            _acrylicConfig.IsInputActive = focused;
        }
        if (focused && !_systemSettingsSubscribed && _started)
        {
            // No change events available on this OS build: pick up high-contrast /
            // colour changes made while the window was in the background.
            ApplyAccessibility();
        }

        if (!_started && focused)
        {
            _started = true;
            await StartAsync().ConfigureAwait(true);
        }

        int epoch = Interlocked.Increment(ref _focusEpoch);
        int presenterEpoch = _presenterEpoch;
        await Task.Delay(180).ConfigureAwait(true);
        if (epoch != _focusEpoch
            || presenterEpoch != _presenterEpoch
            || _surface is null
            || _surface.IsTopLevel
            || _surface.IsBusy
            || _surface.IsInteractiveMove)
        {
            return;
        }

        // The pipeline follows the display, not focus: this only rebuilds when
        // Windows HDR was toggled while the window was in the background.
        await _surface.RefreshOutputPipelineAsync().ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task StartAsync()
    {
        if (_starting)
        {
            return;
        }

        _starting = true;
        try
        {
            Log.Information("Start: version {Version}", AppVersion.Display);
            // The real size is applied by the bind that follows the first load; the
            // bootstrap value only has to be valid, so no waiting for layout here.
            SurfaceLayout layout = _surface?.ReadLayout() ?? ReadLayout();

            DisplayColorCapabilities? display = WindowsAdvancedColor.ForWindow(_hwnd);
            string pipeline = OutputPipeline.Windowed(display?.IsAdvancedColor == true);
            Log.Information(
                "Start: display mode={Mode} peak={Peak} pipeline={Pipeline} layout={Layout}",
                display?.ActiveColorMode,
                display?.SanitizedPeakNits(),
                pipeline,
                layout.CompositionSize);
            SurfaceBootstrapOptions surface = OutputPipeline.Apply(
                new SurfaceBootstrapOptions
                {
                    D3d11OutputMode = "composition",
                    D3d11CompositionSize = layout.IsEmpty ? "64x64" : layout.CompositionSize,
                },
                pipeline,
                display);

            _client = NativeMpvClient.Create();
            _engine = new MpvPlaybackEngine(
                _client,
                new EngineBootstrapOptions
                {
                    Osc = false,
                    OsdBar = false,
                    // PENROSE_MPV_LOG=v|debug traces stream / demuxer activity into the app log.
                    MpvLogLevel = Environment.GetEnvironmentVariable("PENROSE_MPV_LOG") is { Length: > 0 } level ? level : "warn",
                },
                surface,
                new PlaybackPolicyOptions(),
                new SerilogLoggerAdapter("mpv"));
            await _engine.InitializeAsync().ConfigureAwait(true);
            Log.Information("Start: mpv initialized");
            _engine.SnapshotChanged += OnSnapshotChanged;
            _surface = new D3d11CompositionSurface(VideoPanel, _hwnd);
            _surface.TopLevelLeaveRequested += OnTopLevelLeaveRequested;
            _surface.SurfaceGenerationChanged += (_, e) => Log.Information(
                "Surface: bound generation={Generation} pixels={Pixels} dip={Dip}x{DipH} scale={Scale} inverseScale={Inverse} format={Format} csp={Csp}",
                e.Generation,
                _surface?.BoundLayout.CompositionSize,
                (int)(_surface?.BoundLayout.LogicalWidthDip ?? 0),
                (int)(_surface?.BoundLayout.LogicalHeightDip ?? 0),
                _surface?.BoundLayout.CompositionScaleX,
                _surface?.InverseScaleApplied,
                _surface?.CurrentOutputFormat,
                _surface?.CurrentOutputColorSpace);
            await _surface.InitializeAsync(_engine).ConfigureAwait(true);
            // No AttachAsync here: with nothing loaded mpv has no VO and therefore no
            // swap chain, so the bind could only time out (2 s of startup for nothing).
            // PlayPathAsync / PlayLibraryItemAsync attach right after each load.
            Log.Information("Start: surface initialized display={Mode}", _surface.CurrentDisplay?.ActiveColorMode);
            await ApplyHwdecCodecsAsync().ConfigureAwait(true);
            await ApplyDisplayFpsAsync().ConfigureAwait(true);

            _store = new PlaybackStore(AppPaths.Database);
            await _store.InitializeAsync().ConfigureAwait(true);
            _diagnostics = new FileDiagnosticBundleExporter(AppPaths.Logs);

            await LoadSettingsAsync().ConfigureAwait(true);
            _secrets = new DpapiCredentialStore(Path.Combine(AppPaths.Root, "secrets"));
            await TryRestoreLibraryAsync().ConfigureAwait(true);
            RefreshServerList();
            _audioPolicyReady = true;
            await RefreshRecentAsync().ConfigureAwait(true);
            await RefreshChannelsAsync().ConfigureAwait(true);
            Log.Information("Start: ready");
#if DEBUG
            // Lets automated UI checks open dialogs without synthesising input.
            string? open = Environment.GetEnvironmentVariable("PENROSE_DEBUG_OPEN");
            if (open is "settings" or "emby")
            {
                _ = DebugOpenDialogAsync(open);
            }

            // Comma-separated badge names ("Dolby Vision,HDR10,DTS:X"): previews the
            // chips without needing a sample file for every format.
            if (Environment.GetEnvironmentVariable("PENROSE_DEBUG_BADGES") is { Length: > 0 } preview)
            {
                DebugPreviewBadges(preview);
            }

            // "first" or a name fragment: opens the library pane and plays the matching
            // server item, so the Emby path can be exercised end-to-end without input.
            if (Environment.GetEnvironmentVariable("PENROSE_DEBUG_PLAY_LIBRARY") is { Length: > 0 } pick)
            {
                _ = DebugPlayLibraryAsync(pick);
            }
#endif
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Start failed");
            ShowError(ex.Message);
        }
        finally
        {
            _starting = false;
        }

        await DrainPendingOpenAsync().ConfigureAwait(true);
    }

#if DEBUG
    private async Task DebugOpenDialogAsync(string which)
    {
        // XamlRoot only exists once the content has been laid out in the window.
        for (int i = 0; i < 20 && Root.XamlRoot is null; i++)
        {
            await Task.Delay(100).ConfigureAwait(true);
        }

        await Task.Delay(400).ConfigureAwait(true);
        try
        {
            if (which == "emby")
            {
                ShowServersPanel(true);
                ShowServerForm(null);
            }
            else
            {
                ShowSettingsPage();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Debug dialog open failed");
        }
    }

    private async Task DebugPlayLibraryAsync(string pick)
    {
        for (int i = 0; i < 20 && Root.XamlRoot is null; i++)
        {
            await Task.Delay(100).ConfigureAwait(true);
        }

        await Task.Delay(800).ConfigureAwait(true);
        try
        {
            if (_embyLibrary is null)
            {
                Log.Warning("Debug play-library: not connected");
                return;
            }

            await ShowEmbyPageAsync().ConfigureAwait(true);
            // "first" plays the first playable item of the first library; anything
            // else is a server-side search and the first playable hit wins.
            IReadOnlyList<LibraryItem> items;
            if (pick.Equals("first", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<LibraryItem> views = await _embyLibrary.BrowseAsync(null).ConfigureAwait(true);
                items = views.Count > 0 ? await _embyLibrary.BrowseAsync(views[0].Id).ConfigureAwait(true) : [];
            }
            else
            {
                items = await _embyLibrary.SearchAsync(pick).ConfigureAwait(true);
            }

            LibraryItem? item = items.FirstOrDefault(i => !i.IsFolder);
            if (item is null)
            {
                Log.Warning("Debug play-library: no item matches {Pick} among {Count}", pick, items.Count);
                return;
            }

            Log.Information("Debug play-library: {Name} [{Id}]", item.Name, item.Id);
            await PlayLibraryItemAsync(item).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Debug play-library failed");
        }
    }

    private async void DebugPreviewBadges(string list)
    {
        // After the opened file's own chips so the preview wins over them.
        await Task.Delay(3000).ConfigureAwait(true);
        Interlocked.Increment(ref _badgeTicket); // cancels any in-flight fade
        FormatBadgeStrip.Children.Clear();
        TitleBadges.Children.Clear();
        foreach (string badge in list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            FormatBadgeStrip.Children.Add(FormatBadgeVisuals.CreateChip(badge));
            TitleBadges.Children.Add(FormatBadgeVisuals.CreateTitleChip(badge));
        }

        FormatBadgeStrip.Opacity = 1;
        FormatBadgeStrip.Visibility = Visibility.Visible;
    }
#endif

    /// <summary>Recently played local files for the empty state (server items have no reopenable path).</summary>
    private async Task RefreshRecentAsync()
    {
        if (_store is null)
        {
            return;
        }

        List<RecentRow> rows = [];
        try
        {
            IReadOnlyList<PlaybackProgressRecord> recent = await _store.GetRecentAsync(24).ConfigureAwait(true);
            foreach (PlaybackProgressRecord record in recent)
            {
                if (!Uri.TryCreate(record.Uri, UriKind.Absolute, out Uri? uri) || !uri.IsFile)
                {
                    continue;
                }

                string path = uri.LocalPath;
                if (!File.Exists(path))
                {
                    continue;
                }

                string detail = record.DurationMs is > 0 and var total
                    ? Format(TimeSpan.FromMilliseconds(record.PositionMs)) + " / " + Format(TimeSpan.FromMilliseconds(total))
                    : Format(TimeSpan.FromMilliseconds(record.PositionMs));
                rows.Add(new RecentRow(path, Path.GetFileName(path), detail));
                if (rows.Count == 6)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException or IOException)
        {
            Log.Warning(ex, "Recent list unavailable");
        }

        // The most recent file is the "resume" row; the rest are plain entries.
        if (rows.Count > 0)
        {
            ResumeLastText.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.ResumeLast, rows[0].Name);
            ResumeLastDetail.Text = rows[0].Detail;
            ResumeLastButton.Tag = rows[0].Path;
            ResumeLastButton.Visibility = Visibility.Visible;
            SetName(ResumeLastButton, ResumeLastText.Text);
        }
        else
        {
            ResumeLastButton.Visibility = Visibility.Collapsed;
        }

        List<RecentRow> rest = rows.Skip(1).ToList();
        RecentView.ItemsSource = rest;
        RecentView.Visibility = rest.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task DrainPendingOpenAsync()
    {
        while (_pendingOpen.Count > 0)
        {
            string path = _pendingOpen[^1];
            _pendingOpen.Clear();
            await PlayPathAsync(path).ConfigureAwait(true);
        }
    }

    private async Task LoadSettingsAsync()
    {
        if (_store is null || _engine is null)
        {
            return;
        }

        _settings = SimpleSettingsSerializer.FromJson(await _store.GetSettingAsync(SimpleSettingsSerializer.StoreKey)
            .ConfigureAwait(true));
        ApplyLanguage();
        _audioPolicy = _settings.AudioPolicy;
        _night = _settings.NightMode;
        _muted = _settings.Mute;
        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 100);
        SetVolumeIcon();
        _speed = _settings.Speed <= 0 ? 1 : _settings.Speed;
        _subDelay = _settings.SubDelaySeconds;
        // Each group is independent: one option mpv rejects (a stale value in
        // settings, an option renamed by a libmpv upgrade) must not abort the rest
        // of startup and leave the library / audio policy uninitialised.
        await ApplySettingGroupAsync("playback policy", () => ApplyPlaybackPolicyAsync(save: false)).ConfigureAwait(true);
        await ApplySettingGroupAsync("quality preset", () => ApplyQualityAsync(save: false)).ConfigureAwait(true);
        await ApplySettingGroupAsync("decoding", () => ApplyDecodingAsync(save: false)).ConfigureAwait(true);
        await ApplySettingGroupAsync("speed", () => ApplySpeedAsync(save: false)).ConfigureAwait(true);
        await ApplySettingGroupAsync("subtitle delay", () => ApplySubDelayAsync(save: false)).ConfigureAwait(true);
        await ApplySettingGroupAsync("subtitle style", () => ApplySubtitleStyleAsync(save: false)).ConfigureAwait(true);
        await ApplySettingGroupAsync("volume", () => SetVolumeAsync(VolumeSlider.Value)).ConfigureAwait(true);
        await ApplySettingGroupAsync("mute", () => _engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["mute"] = _muted ? "yes" : "no",
        })).ConfigureAwait(true);
    }

    private async Task ApplySettingGroupAsync(string name, Func<Task> apply)
    {
        try
        {
            await apply().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is MpvException or InvalidOperationException)
        {
            Log.Warning(ex, "Settings: {Group} not applied", name);
        }
    }

    private async void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_surface is null)
        {
            return;
        }

        int epoch = Interlocked.Increment(ref _resizeEpoch);
        await Task.Delay(120).ConfigureAwait(true);
        if (epoch != _resizeEpoch
            || _surface is null
            || _surface.IsTopLevel
            || _surface.IsBusy
            || _surface.IsInteractiveMove)
        {
            return;
        }

        await _surface.ResizeAsync(_surface.ReadLayout()).ConfigureAwait(true);
    }

    private async void OnCompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (_surface is null || _surface.IsBusy || _surface.IsTopLevel || _surface.IsInteractiveMove)
        {
            return;
        }

        await _surface.RecreateAsync(SurfaceRecreateReason.CompositionScaleChanged).ConfigureAwait(true);
    }

    private async void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange)
        {
            ApplyCaptionColors();
        }

        if (args.DidPresenterChange)
        {
            if (!IsPip())
            {
                _pipMini = false;
                ExtendsContentIntoTitleBar = true;
                ApplyCaptionColors();
                ShowChrome();
            }

            _ = IgnoreFocusDuringPresenterChangeAsync(Interlocked.Increment(ref _presenterEpoch));
        }

        if (_surface is null || (!args.DidPositionChange && !args.DidSizeChange))
        {
            return;
        }

        if (_surface.IsInteractiveMove)
        {
            return;
        }

        if (!_surface.HasMoveTracker)
        {
            _surface.BeginInteractiveMove();
            int moveEpoch = Interlocked.Increment(ref _displayEpoch);
            await Task.Delay(200).ConfigureAwait(true);
            if (moveEpoch != _displayEpoch || _surface is null)
            {
                return;
            }

            await _surface.EndInteractiveMoveAsync().ConfigureAwait(true);
            await ApplyDisplayFpsAsync().ConfigureAwait(true);
            await RefreshStatusAsync().ConfigureAwait(true);
            return;
        }

        int epoch = Interlocked.Increment(ref _displayEpoch);
        await Task.Delay(150).ConfigureAwait(true);
        if (epoch != _displayEpoch || _surface is null || _surface.IsInteractiveMove)
        {
            return;
        }

        await _surface.RefreshDisplayTargetsAsync().ConfigureAwait(true);
        await ApplyDisplayFpsAsync().ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task IgnoreFocusDuringPresenterChangeAsync(int presenterEpoch)
    {
        await Task.Delay(400).ConfigureAwait(true);
        if (presenterEpoch != _presenterEpoch || _surface is null || _surface.IsBusy)
        {
            return;
        }

        await _surface.RefreshOutputPipelineAsync().ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        DispatcherQueue.TryEnqueue(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        if (!_seeking && snapshot.Duration is { } duration && duration.TotalSeconds > 0)
        {
            PositionSlider.Maximum = duration.TotalSeconds;
            PositionSlider.Value = snapshot.Position?.TotalSeconds ?? 0;
            MiniProgress.Maximum = duration.TotalSeconds;
            MiniProgress.Value = snapshot.Position?.TotalSeconds ?? 0;
        }

        TimeText.Text = Format(snapshot.Position) + " / " + Format(snapshot.Duration);
        SetPlayIcon(IsPlaying(snapshot));
        SleepPreventer.SetPlaying(snapshot.PlaybackIntent == PlaybackIntent.Playing);
        UpdateTransportVisibility();
        if (snapshot.PausedForCache)
        {
            ShowOsd(snapshot.BufferingPercent is { } percent
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.BufferingPercent, percent)
                : _ui.Buffering);
        }

        if (snapshot.MediaPhase == MediaPhase.Failed)
        {
            ShowError(snapshot.Error ?? _ui.PlayFailed);
        }

        // Shortly before an episode ends, resolve the next one and touch both ends
        // of its file, so auto-continue does not sit through the server's cold open.
        // Close to the end on purpose: the server forgets a warmed region within a
        // minute or two.
        if (!_prefetchStarted
            && _currentLibraryItem is { Kind: "Episode" }
            && snapshot.MediaPhase == MediaPhase.Loaded
            && snapshot.Duration is { TotalSeconds: > 90 } total
            && snapshot.Position is { } at
            && total - at < TimeSpan.FromSeconds(45))
        {
            _prefetchStarted = true;
            _ = PrefetchNextEpisodeAsync(_currentLibraryItem, snapshot.PlaybackGeneration);
        }

        if (snapshot.MediaPhase == MediaPhase.Ended
            && snapshot.PlaybackGeneration != _endedGeneration
            && !_suppressPlaylistAdvance)
        {
            _endedGeneration = snapshot.PlaybackGeneration;
            Log.Information(
                "Play: ended generation={Generation} playlist={Index}/{Count}",
                snapshot.PlaybackGeneration,
                _playlistIndex + 1,
                _playlist.Count);
            _ = AdvanceAfterEndAsync();
        }
    }

    /// <summary>
    /// End of media: the last position is cleared (resume from the start next
    /// time), the Emby session is closed, then the playlist moves on. Sequential so
    /// the Stop report is not racing the next item's Start.
    /// </summary>
    private async Task AdvanceAfterEndAsync()
    {
        try
        {
            await SaveProgressAsync().ConfigureAwait(true);
            await StopLibrarySessionAsync().ConfigureAwait(true);
            if (_currentLibraryItem is { Kind: "Episode" } episode)
            {
                await PlayNeighbourEpisodeAsync(episode, +1, auto: true).ConfigureAwait(true);
                return;
            }

            await PlayRelativeAsync(1).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Advance after end failed");
        }
    }

    /// <summary>
    /// Next / previous episode of the series on the server; <paramref name="auto"/>
    /// is the end-of-episode continue, which also uses the prefetched candidate.
    /// </summary>
    private async Task PlayNeighbourEpisodeAsync(LibraryItem episode, int delta, bool auto)
    {
        if (_embyLibrary is null)
        {
            return;
        }

        LibraryItem? next = null;
        PlaybackCandidate? resolved = null;
        if (delta > 0 && _prefetchedNext is { } ready && ready.Item.SeriesId == episode.SeriesId)
        {
            (next, resolved) = ready;
        }

        try
        {
            next ??= await _embyLibrary.NeighbourEpisodeAsync(episode, delta).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Neighbour episode lookup failed");
        }

        if (next is null)
        {
            if (!auto)
            {
                ShowOsd(delta > 0 ? _ui.NoNextEpisode : _ui.NoPreviousEpisode);
            }

            return;
        }

        Log.Information("Play: {Mode} episode {Name} [{Id}] prefetched={Prefetched}", auto ? "auto-continue" : "step", next.Name, next.Id, resolved is not null);
        ShowOsd(string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.NextEpisodeOsd, next.EpisodeCode ?? next.Name));
        await PlayLibraryItemAsync(next, resolved, _libraryQueue).ConfigureAwait(true);
    }

    private async Task PrefetchNextEpisodeAsync(LibraryItem episode, long generation)
    {
        if (_embyLibrary is null || _embyResolver is null || _emby is null || _engine is null)
        {
            return;
        }

        try
        {
            LibraryItem? next = await _embyLibrary.NeighbourEpisodeAsync(episode, +1).ConfigureAwait(true);
            if (next is null || _engine.Snapshot.PlaybackGeneration != generation)
            {
                return;
            }

            string? hwdec = await _engine.GetPropertyStringAsync("hwdec-current").ConfigureAwait(true);
            PlaybackCapabilitySnapshot caps = DeviceProfileBuilder.FromRuntime(
                _audioPolicy, _settings.AudioPassthrough, _settings.AudioDevice, DisplayIsAdvancedColor(), hwdec, _surface?.CurrentDisplay?.AdapterLuid);
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            PlaybackCandidate candidate = await _embyResolver.ResolveAsync(next.Id, caps).ConfigureAwait(true);
            long resolved = clock.ElapsedMilliseconds;
            bool warm = !candidate.Uri.IsFile
                && await _emby.WarmAsync(candidate.Uri, candidate.Headers, tail: candidate.Method != PlayMethod.Transcode).ConfigureAwait(true);
            if (_engine.Snapshot.PlaybackGeneration == generation)
            {
                _prefetchedNext = (next, candidate);
            }

            Log.Information(
                "Play: prefetched next episode {Name} [{Id}] resolve={ResolveMs}ms warm={Warm} in {WarmMs}ms",
                next.Name, next.Id, resolved, warm, clock.ElapsedMilliseconds - resolved);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Prefetch of the next episode failed");
        }
    }

    private async Task PlayPathAsync(string path, bool replacePlaylist = true)
    {
        if (_engine is null || _store is null)
        {
            return;
        }

        Interlocked.Increment(ref _matchTicket);
        _chapters = [];
        // A local file leaves the server queue behind.
        _currentLibraryItem = null;
        _prefetchedNext = null;
        _prefetchStarted = false;

        if (LocalPlaybackFactory.IsSubtitlePath(path))
        {
            await _engine.ExecuteCommandAsync(["sub-add", new Uri(Path.GetFullPath(path)).AbsoluteUri, "auto"])
                .ConfigureAwait(true);
            return;
        }

        if (replacePlaylist)
        {
            ReplacePlaylistFromInput(path);
            RefreshPlaylistPane();
            string trimmed = path.Trim().Trim('"');
            if (Directory.Exists(trimmed) && LocalPlaybackFactory.ResolveDiscRoot(trimmed) is null)
            {
                if (_playlist.Count == 0)
                {
                    ShowError(_ui.EmptyFolder);
                    return;
                }

                path = _playlist[_playlistIndex];
            }
            else if (LocalPlaybackFactory.ResolveDiscRoot(trimmed) is { } disc)
            {
                path = disc;
            }
        }

        _suppressPlaylistAdvance = true;
        try
        {
            if (_surface is { IsTopLevel: true })
            {
                await LeaveTopLevelAsync().ConfigureAwait(true);
            }

            await SaveProgressAsync().ConfigureAwait(true);
            await StopLibrarySessionAsync().ConfigureAwait(true);
            await RestoreDisplayRefreshAsync().ConfigureAwait(true);
            await RestoreWindowsHdrAsync().ConfigureAwait(true);
            HideThumbPreview();
            PlaybackRequest request = LocalPlaybackFactory.FromUserInput(path);
            string progressKey = request.Uri.AbsoluteUri;
            PlaybackProgressRecord? saved = _settings.RememberPlaybackPosition
                ? await _store.GetProgressAsync(progressKey).ConfigureAwait(true)
                : null;
            if (saved is not null && PlaybackResume.ShouldRestore(saved.PositionMs, saved.DurationMs))
            {
                request = request with { StartPosition = TimeSpan.FromMilliseconds(saved.PositionMs) };
            }

            _currentUri = request.Uri;
            _currentSourceKind = request.SourceKind;
            _progressKey = progressKey;
            HideError();
            await ResetAudioDelayAsync().ConfigureAwait(true);
            PlaybackSnapshot loaded = await _engine.LoadAsync(request).ConfigureAwait(true);
            Log.Information(
                "Play: loaded {Name} generation={Generation} duration={Duration} start={Start}",
                Path.GetFileName(Uri.UnescapeDataString(request.Uri.AbsolutePath)),
                loaded.PlaybackGeneration,
                loaded.Duration,
                request.StartPosition);
            EmptyState.Visibility = Visibility.Collapsed;
            HideEmbyPage();
            UpdateTransportVisibility();
            SetMediaTitle(await MediaTitleAsync().ConfigureAwait(true));
            if (_surface is { IsTopLevel: false })
            {
                await _surface.AttachAsync().ConfigureAwait(true);
            }

            _ = ShowFormatBadgesAsync();
            await RefreshChaptersAsync().ConfigureAwait(true);
            await MaybePickDiscTitleAsync(path).ConfigureAwait(true);
            StartSubtitleMatch(path);
            RefreshPlaylistPane();
            await MatchDisplayRefreshAsync().ConfigureAwait(true);
            await MaybeApplyFullscreenAfterLoadAsync().ConfigureAwait(true);
            await RefreshStatusAsync().ConfigureAwait(true);
            ShowChrome();
            RestartHideTimer();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Play failed");
            ShowError(ex.Message);
        }
        finally
        {
            _suppressPlaylistAdvance = false;
        }
    }

    private async Task OpenFileAsync()
    {
        FileOpenPicker picker = new();
        nint hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await PlayPathAsync(file.Path).ConfigureAwait(true);
        }

        Root.Focus(FocusState.Programmatic);
    }

    private async Task OpenFolderAsync()
    {
        FolderPicker picker = new();
        nint hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await PlayPathAsync(folder.Path).ConfigureAwait(true);
        }

        Root.Focus(FocusState.Programmatic);
    }

    private async Task OpenUrlAsync()
    {
        TextBox box = new()
        {
            PlaceholderText = _ui.OpenUrlPlaceholder,
            AcceptsReturn = false,
        };
        ContentDialog dialog = new()
        {
            Title = _ui.OpenUrl,
            PrimaryButtonText = _ui.Play,
            CloseButtonText = _ui.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            Content = box,
            XamlRoot = Root.XamlRoot,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        Root.Focus(FocusState.Programmatic);
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            await PlayPathAsync(box.Text.Trim()).ConfigureAwait(true);
        }
    }

    private async Task OpenSubtitleAsync()
    {
        FileOpenPicker picker = new();
        nint hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".ass");
        picker.FileTypeFilter.Add(".ssa");
        picker.FileTypeFilter.Add(".srt");
        picker.FileTypeFilter.Add(".vtt");
        picker.FileTypeFilter.Add(".sub");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await PlayPathAsync(file.Path).ConfigureAwait(true);
        }

        Root.Focus(FocusState.Programmatic);
    }

    private async Task PlayRelativeAsync(int delta)
    {
        if (_currentLibraryItem is { } current)
        {
            if (current.Kind == "Episode")
            {
                await PlayNeighbourEpisodeAsync(current, delta, auto: false).ConfigureAwait(true);
                return;
            }

            // Movies etc.: walk the listing the item was started from.
            int at = _libraryQueue.ToList().FindIndex(i => i.Id == current.Id);
            int target = at + delta;
            if (at >= 0 && target >= 0 && target < _libraryQueue.Count)
            {
                await PlayLibraryItemAsync(_libraryQueue[target], null, _libraryQueue).ConfigureAwait(true);
            }

            return;
        }

        int next = _playlistIndex + delta;
        if (next < 0 || next >= _playlist.Count)
        {
            return;
        }

        _playlistIndex = next;
        await PlayPathAsync(_playlist[next], replacePlaylist: false).ConfigureAwait(true);
    }

    private async Task TogglePauseAsync()
    {
        if (_engine is null)
        {
            return;
        }

        if (_engine.Snapshot.PlaybackIntent == PlaybackIntent.Playing)
        {
            await _engine.PauseAsync().ConfigureAwait(true);
            await SaveProgressAsync(paused: true).ConfigureAwait(true);
        }
        else
        {
            await _engine.ResumeAsync().ConfigureAwait(true);
            await SaveProgressAsync(paused: false).ConfigureAwait(true);
        }
    }

    private async Task CommitSeekAsync()
    {
        if (!_seeking)
        {
            return;
        }

        _seeking = false;
        if (_engine is null)
        {
            return;
        }

        await _engine.SeekAsync(TimeSpan.FromSeconds(PositionSlider.Value)).ConfigureAwait(true);
    }

    private void OnSeekHover(object sender, PointerRoutedEventArgs e)
    {
        if (_engine?.Snapshot.Duration is not { } duration || duration <= TimeSpan.Zero)
        {
            HideThumbPreview();
            return;
        }

        double x = e.GetCurrentPoint(PositionSlider).Position.X;
        TimeSpan? raw = ThumbnailHover.TimeAt(x, PositionSlider.ActualWidth, duration);
        if (raw is null)
        {
            HideThumbPreview();
            return;
        }

        TimeSpan at = ThumbnailHover.Quantize(raw.Value);
        ThumbTime.Text = Format(at);
        ThumbPreview.Visibility = Visibility.Visible;
        double pointerX = e.GetCurrentPoint(Root).Position.X;
        double left = Math.Clamp(pointerX - 86, 8, Math.Max(8, Root.ActualWidth - 180));
        // Just above the slider (which sits ~60 dip from the bottom edge of the bar).
        ThumbPreview.Margin = new Thickness(left, 0, 0, 92);
        if (!_settings.SeekThumbnails
            || _reduceMotion
            || !ThumbnailHover.IsLocalFile(_currentUri)
            || _thumbAt == at)
        {
            if (_thumbAt != at)
            {
                ThumbImage.Source = null;
            }

            _thumbAt = at;
            return;
        }

        _thumbAt = at;
        int ticket = Interlocked.Increment(ref _thumbTicket);
        Uri uri = _currentUri!;
        _ = CaptureThumbAsync(ticket, uri.LocalPath, at);
    }

    private void HideThumbPreview()
    {
        Interlocked.Increment(ref _thumbTicket);
        _thumbAt = null;
        ThumbPreview.Visibility = Visibility.Collapsed;
        ThumbImage.Source = null;
    }

    private async Task CaptureThumbAsync(int ticket, string path, TimeSpan at)
    {
        try
        {
            await Task.Delay(ThumbnailHover.DebounceMilliseconds).ConfigureAwait(true);
            if (ticket != _thumbTicket)
            {
                return;
            }

            _thumbs ??= new MpvThumbnailGrabber(Path.Combine(AppPaths.Root, "thumbs"));
            string output = Path.Combine(AppPaths.Root, "thumbs", "t" + (ticket % ThumbRingSize) + ".jpg");
            string? file = await _thumbs.CaptureAsync(path, at, output).ConfigureAwait(true);
            if (ticket != _thumbTicket || string.IsNullOrWhiteSpace(file))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (ticket != _thumbTicket)
                {
                    return;
                }

                BitmapImage image = new()
                {
                    DecodePixelWidth = 160,
                    CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                    UriSource = new Uri(file),
                };
                ThumbImage.Source = image;
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Thumbnail capture failed");
        }
    }

    private async Task SetVolumeAsync(double value)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["volume"] = value.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
        }).ConfigureAwait(true);
        SetVolumeIcon();
        ShowOsd(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            _ui.VolumeOsd,
            (int)Math.Round(value)));
    }

    private async Task ToggleMuteAsync()
    {
        if (_engine is null)
        {
            return;
        }

        _muted = !_muted;
        await _engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["mute"] = _muted ? "yes" : "no",
        }).ConfigureAwait(true);
        SetVolumeIcon();
        ShowOsd(_muted ? _ui.Mute : _ui.Unmute);
        await SaveSettingsAsync().ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task ShowTrackFlyoutAsync(Button button, string type)
    {
        if (_engine is null)
        {
            return;
        }

        string? json = await _engine.GetPropertyStringAsync("track-list").ConfigureAwait(true);
        IReadOnlyList<TrackInfo> tracks = TrackListParser.Parse(json);
        MenuFlyout flyout = new();
        if (type == "sub")
        {
            MenuFlyoutItem off = new() { Text = _ui.SubOff };
            off.Click += async (_, _) => await SetTrackAsync("sid", "no").ConfigureAwait(true);
            flyout.Items.Add(off);
            MenuFlyoutItem load = new() { Text = _ui.LoadExternalSub };
            load.Click += async (_, _) => await OpenSubtitleAsync().ConfigureAwait(true);
            flyout.Items.Add(load);
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (TrackInfo track in TrackListParser.OfType(tracks, type))
        {
            string property = type == "audio" ? "aid" : "sid";
            long id = track.Id;
            MenuFlyoutItem item = new() { Text = FormatTrack(track) };
            item.Click += async (_, _) =>
                await SetTrackAsync(property, id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .ConfigureAwait(true);
            flyout.Items.Add(item);
        }

        if (type == "sub")
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            MenuFlyoutItem secondaryOff = new() { Text = _ui.SecondaryOff };
            secondaryOff.Click += async (_, _) => await SetTrackAsync("secondary-sid", "no").ConfigureAwait(true);
            flyout.Items.Add(secondaryOff);
            foreach (TrackInfo track in TrackListParser.OfType(tracks, "sub"))
            {
                long id = track.Id;
                MenuFlyoutItem item = new() { Text = _ui.SecondaryPrefix + FormatTrack(track) };
                item.Click += async (_, _) =>
                    await SetTrackAsync("secondary-sid", id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .ConfigureAwait(true);
                flyout.Items.Add(item);
            }
        }

        if (flyout.Items.Count == 0 || (type == "sub" && flyout.Items.Count == 3))
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = _ui.NoTracks, IsEnabled = false });
        }

        if (type == "audio")
        {
            // Output policy, passthrough and night mode live with the audio tracks
            // instead of taking permanent space in the transport bar.
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(new MenuFlyoutItem { Text = _ui.AudioOutput, IsEnabled = false });
            (AudioPolicy Policy, string Label)[] policies =
            [
                (AudioPolicy.SystemCompatible, _ui.AudioSystem),
                (AudioPolicy.ForceStereo, _ui.AudioStereo),
                (AudioPolicy.HomeTheaterPcm, _ui.AudioHomePcm),
            ];
            foreach ((AudioPolicy policy, string label) in policies)
            {
                RadioMenuFlyoutItem item = new()
                {
                    Text = label,
                    GroupName = "audio-policy",
                    IsChecked = policy == _audioPolicy,
                };
                item.Click += async (_, _) => await SelectAudioPolicyAsync(policy).ConfigureAwait(true);
                flyout.Items.Add(item);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());
            ToggleMenuFlyoutItem passthrough = new()
            {
                Text = _ui.AudioPassthrough,
                IsChecked = _settings.AudioPassthrough,
            };
            passthrough.Click += async (_, _) => await TogglePassthroughAsync().ConfigureAwait(true);
            flyout.Items.Add(passthrough);
            ToggleMenuFlyoutItem night = new()
            {
                Text = _ui.Night,
                IsChecked = _night,
                IsEnabled = !_settings.AudioPassthrough,
                Icon = new FontIcon { Glyph = "\uE708" },
            };
            night.Click += async (_, _) => await ToggleNightAsync().ConfigureAwait(true);
            flyout.Items.Add(night);

            // Per-file A/V sync: the header shows the current offset, the steps show their shortcuts.
            double delay = await ReadAudioDelayAsync().ConfigureAwait(true);
            string step = AudioDelay.StepMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    _ui.AudioDelayOsd,
                    AudioDelay.FormatMilliseconds(delay)),
                IsEnabled = false,
            });
            MenuFlyoutItem earlier = new()
            {
                Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.AudioDelayEarlier, step),
                KeyboardAcceleratorTextOverride = "Ctrl+-",
            };
            earlier.Click += async (_, _) => await NudgeAudioDelayAsync(-AudioDelay.Step).ConfigureAwait(true);
            flyout.Items.Add(earlier);
            MenuFlyoutItem later = new()
            {
                Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.AudioDelayLater, step),
                KeyboardAcceleratorTextOverride = "Ctrl+=",
            };
            later.Click += async (_, _) => await NudgeAudioDelayAsync(AudioDelay.Step).ConfigureAwait(true);
            flyout.Items.Add(later);
            MenuFlyoutItem reset = new() { Text = _ui.AudioDelayReset, IsEnabled = delay != 0 };
            reset.Click += async (_, _) => await SetAudioDelayAsync(0).ConfigureAwait(true);
            flyout.Items.Add(reset);
        }

        flyout.ShowAt(button);
    }

    private void TogglePlaylistPane()
    {
        bool show = PlaylistPane.Visibility != Visibility.Visible;
        PlaylistPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            RefreshPlaylistPane();
            _chromePinned = true;
            ShowChrome();
            return;
        }

        _chromePinned = false;
        RestartHideTimer();
    }

    private void RefreshPlaylistPane()
    {
        List<PlaylistRow> rows = new(_playlist.Count);
        for (int i = 0; i < _playlist.Count; i++)
        {
            rows.Add(new PlaylistRow(i, Path.GetFileName(_playlist[i].TrimEnd('\\', '/')), i == _playlistIndex));
        }

        PlaylistView.ItemsSource = rows;
        PlaylistHeader.Text = _playlist.Count > 0
            ? $"{_ui.Playlist}  {_playlistIndex + 1}/{_playlist.Count}"
            : _ui.Playlist;
        if (_playlistIndex >= 0 && _playlistIndex < rows.Count)
        {
            PlaylistView.SelectedIndex = _playlistIndex;
            PlaylistView.ScrollIntoView(rows[_playlistIndex]);
        }
    }

    private async Task OnPlaylistItemClickAsync(ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PlaylistRow row || row.Index < 0 || row.Index >= _playlist.Count)
        {
            return;
        }

        _playlistIndex = row.Index;
        await PlayPathAsync(_playlist[row.Index], replacePlaylist: false).ConfigureAwait(true);
    }

    private async Task ShowChapterFlyoutAsync()
    {
        if (_chapters.Count == 0)
        {
            await RefreshChaptersAsync().ConfigureAwait(true);
        }

        MenuFlyout flyout = new();
        if (_chapters.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = _ui.NoChapters, IsEnabled = false });
            flyout.ShowAt(ChapterButton);
            return;
        }

        foreach (ChapterInfo chapter in _chapters)
        {
            TimeSpan start = chapter.Start;
            string label = (string.IsNullOrWhiteSpace(chapter.Title)
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.ChapterFallback, chapter.Index + 1)
                    : chapter.Title)
                + "  " + Format(start);
            MenuFlyoutItem item = new() { Text = label };
            item.Click += async (_, _) =>
            {
                if (_engine is not null)
                {
                    await _engine.SeekAsync(start).ConfigureAwait(true);
                }
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(ChapterButton);
    }

    private async Task RefreshChaptersAsync()
    {
        if (_engine is null)
        {
            _chapters = [];
            return;
        }

        string? json = await _engine.GetPropertyStringAsync("chapter-list").ConfigureAwait(true);
        _chapters = ChapterListParser.Parse(json);
    }

    private async Task MaybePickDiscTitleAsync(string path)
    {
        if (_engine is null || LocalPlaybackFactory.ResolveDiscRoot(path) is null)
        {
            return;
        }

        string? raw = await _engine.GetPropertyStringAsync("disc-title-list").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = await _engine.GetPropertyStringAsync("disc-titles").ConfigureAwait(true);
        }

        IReadOnlyList<DiscTitleInfo> titles = DiscTitleParser.Parse(raw);
        if (titles.Count <= 1)
        {
            return;
        }

        ListView list = new();
        foreach (DiscTitleInfo title in titles)
        {
            list.Items.Add(new ListViewItem { Content = title.Label, Tag = title.Id });
        }

        list.SelectedIndex = 0;
        ContentDialog dialog = new()
        {
            Title = _ui.PickDiscTitle,
            PrimaryButtonText = _ui.Play,
            CloseButtonText = _ui.Skip,
            Content = list,
            XamlRoot = Root.XamlRoot,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        Root.Focus(FocusState.Programmatic);
        if (result != ContentDialogResult.Primary
            || list.SelectedItem is not ListViewItem { Tag: int id }
            || id <= 0)
        {
            return;
        }

        await _engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["disc-title"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).ConfigureAwait(true);
        SetMediaTitle(await MediaTitleAsync().ConfigureAwait(true));
        await RefreshChaptersAsync().ConfigureAwait(true);
        ShowOsd(string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.DiscTitleOsd, id));
    }

    private void StartSubtitleMatch(string path)
    {
        if (_engine is null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        int ticket = _matchTicket;
        string videoPath = Path.GetFullPath(path);
        _ = AttachMatchedSubtitlesAsync(videoPath, ticket);
    }

    private async Task AttachMatchedSubtitlesAsync(string videoPath, int ticket)
    {
        IReadOnlyList<string> extra;
        try
        {
            extra = await Task.Run(() => LocalFileMatcher.MatchExtraSubtitles(videoPath)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Subtitle match failed");
            return;
        }

        if (ticket != _matchTicket || _engine is null)
        {
            return;
        }

        foreach (string sub in extra)
        {
            if (ticket != _matchTicket)
            {
                return;
            }

            await _engine.ExecuteCommandAsync(["sub-add", new Uri(sub).AbsoluteUri, "auto"])
                .ConfigureAwait(true);
        }
    }

    private async Task ApplyHwdecCodecsAsync()
    {
        if (_engine is null)
        {
            return;
        }

        string? current = await _engine.GetPropertyStringAsync("hwdec-codecs").ConfigureAwait(true);
        string next = HwdecCodecPolicy.Intersect(current);
        await _engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["hwdec-codecs"] = next,
        }).ConfigureAwait(true);
    }

    private async Task ApplyDisplayFpsAsync()
    {
        if (_engine is null)
        {
            return;
        }

        double hz = _surface?.CurrentRefreshRateHz ?? 0;
        if (hz is < 20 or > 240)
        {
            return;
        }

        await _engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["display-fps-override"] = hz.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        }).ConfigureAwait(true);
    }

    private async Task MatchDisplayRefreshAsync()
    {
        if (!_settings.MatchDisplayRefresh || _engine is null)
        {
            return;
        }

        string? raw = await _engine.GetPropertyStringAsync("container-fps").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = await _engine.GetPropertyStringAsync("estimated-vf-fps").ConfigureAwait(true);
        }

        if (!double.TryParse(
                raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double fps))
        {
            return;
        }

        string? device = DisplayRefreshSwitcher.DeviceName(_hwnd);
        int current = DisplayRefreshSwitcher.CurrentHertz(device);
        IReadOnlyList<int> available = DisplayRefreshSwitcher.AvailableHertz(device);
        int? next = RefreshRateMatch.Pick(fps, current, available);
        if (next is null)
        {
            return;
        }

        // A change already applied on another monitor is undone first; only one
        // display is ever left in a temporary mode.
        if (_refreshChange is { } previous && !string.Equals(previous.DeviceName, device, StringComparison.OrdinalIgnoreCase))
        {
            DisplayRefreshSwitcher.TryRestore(previous);
            _refreshChange = null;
        }

        if (!DisplayRefreshSwitcher.TrySetHertz(_hwnd, next.Value, out RefreshChange? change) || change is null)
        {
            return;
        }

        // Keep the original rate if this is the second switch on the same monitor.
        _refreshChange = _refreshChange is { } same && string.Equals(same.DeviceName, change.DeviceName, StringComparison.OrdinalIgnoreCase)
            ? same with { Hertz = change.Hertz }
            : change;
        await ApplyDisplayFpsAsync().ConfigureAwait(true);
        ShowOsd(string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.RefreshHzOsd, next.Value));
    }

    private async Task RestoreDisplayRefreshAsync()
    {
        if (_refreshChange is not { } change)
        {
            return;
        }

        _refreshChange = null;
        DisplayRefreshSwitcher.TryRestore(change);
        await ApplyDisplayFpsAsync().ConfigureAwait(true);
    }

    private async Task MaybeApplyWindowsHdrAsync(string? gamma, int? dolbyVisionProfile)
    {
        if (!_settings.AutoEnableWindowsHdr || _engine is null)
        {
            return;
        }

        int ticket = _badgeTicket;
        long generation = _engine.Snapshot.PlaybackGeneration;
        bool sourceHdr = HdrSource.IsSourceHdr(gamma, dolbyVisionProfile);
        WindowsAdvancedColor.AdvancedColorState state = WindowsAdvancedColor.StateForWindow(_hwnd);
        bool enable = WindowsHdrPolicy.ShouldEnable(
            _settings.AutoEnableWindowsHdr,
            sourceHdr,
            state.Supported,
            state.Enabled);
        bool refresh = WindowsHdrPolicy.ShouldRefreshPipeline(sourceHdr, state.Enabled);
        Log.Information(
            "HDR: source={SourceHdr} gamma={Gamma} dv={DolbyVision} windows={Enabled} enable={Enable} refresh={Refresh}",
            sourceHdr,
            gamma,
            dolbyVisionProfile,
            state.Enabled,
            enable,
            refresh);
        if (!enable && !refresh)
        {
            return;
        }

        if (enable)
        {
            if (!WindowsAdvancedColor.TrySetForWindow(_hwnd, enable: true, out WindowsAdvancedColor.AdvancedColorTarget target))
            {
                return;
            }

            _hdrTarget = target;
            await Task.Delay(400).ConfigureAwait(true);
            if (_engine is null
                || ticket != _badgeTicket
                || _engine.Snapshot.PlaybackGeneration != generation)
            {
                return;
            }
        }

        if (_surface is not null && !_surface.IsTopLevel && !_surface.IsBusy)
        {
            await _surface.RefreshOutputPipelineAsync().ConfigureAwait(true);
        }

        if (enable)
        {
            ShowOsd(_ui.HdrOnOsd);
        }
    }

    private async Task RestoreWindowsHdrAsync()
    {
        if (_hdrTarget is not { } target)
        {
            return;
        }

        _hdrTarget = null;
        // Restore on the display we changed, not on whichever one the window sits on now.
        WindowsAdvancedColor.TrySetForTarget(target, enable: false);
        await Task.Delay(400).ConfigureAwait(true);
        if (_surface is not null && !_surface.IsTopLevel && !_surface.IsBusy)
        {
            await _surface.RefreshOutputPipelineAsync().ConfigureAwait(true);
        }
    }

    private async Task SetTrackAsync(string property, string value)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.ExecuteCommandAsync(["set", property, value]).ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
        if (property == "aid")
        {
            _ = ShowFormatBadgesAsync();
        }
    }

    private async Task CycleAsync(string track)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.ExecuteCommandAsync(["cycle", track]).ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
        if (track == "aid")
        {
            _ = ShowFormatBadgesAsync();
        }
    }

    private async Task ToggleNightAsync()
    {
        if (_engine is null)
        {
            return;
        }

        if (_settings.AudioPassthrough)
        {
            // The flyout greys the item out; only the N key reaches here.
            ShowOsd(_ui.NightBlockedByPassthrough);
            return;
        }

        _night = !_night;
        await ApplyPlaybackPolicyAsync().ConfigureAwait(true);
        ShowOsd(_night ? _ui.Night + "  ✓" : _ui.Night + "  ✕");
    }

    private async Task SelectAudioPolicyAsync(AudioPolicy next)
    {
        if (!_audioPolicyReady || _engine is null || next == _audioPolicy)
        {
            return;
        }

        _audioPolicy = next;
        await ApplyPlaybackPolicyAsync().ConfigureAwait(true);
        await SettleAudioAsync().ConfigureAwait(true);
        ShowOsd(AudioPolicyLabel(next));
    }

    private async Task TogglePassthroughAsync()
    {
        if (!_audioPolicyReady || _engine is null)
        {
            return;
        }

        _settings = _settings with { AudioPassthrough = !_settings.AudioPassthrough };
        await ApplyPassthroughChangedAsync().ConfigureAwait(true);
        ShowOsd(_ui.AudioPassthrough + (_settings.AudioPassthrough ? "  ✓" : "  ✕"));
    }

    /// <summary>Pushes a changed <see cref="SimpleSettings.AudioPassthrough"/> to mpv and reads the chain back.</summary>
    private async Task ApplyPassthroughChangedAsync()
    {
        await ApplyPlaybackPolicyAsync().ConfigureAwait(true);
        await SettleAudioAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// audio-* changes reinit the AO asynchronously; output params read straight
    /// after the property set are stale, so wait before refreshing the chips.
    /// </summary>
    private async Task SettleAudioAsync()
    {
        await Task.Delay(350).ConfigureAwait(true);
        await RefreshChannelsAsync().ConfigureAwait(true);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private string AudioPolicyLabel(AudioPolicy policy) =>
        policy switch
        {
            AudioPolicy.ForceStereo => _ui.AudioStereo,
            AudioPolicy.HomeTheaterPcm => _ui.AudioHomePcm,
            _ => _ui.AudioSystem,
        };

    /// <summary>
    /// Reads decoded vs. output layout ("7.1 → 5.1" when downmixing) into the
    /// transport-bar chip. Output params exist only once audio is playing, so this
    /// is called after load and again after a layout change settles.
    /// </summary>
    private async Task RefreshChannelsAsync()
    {
        RefreshHdrChip();
        await RefreshPassthroughStateAsync().ConfigureAwait(true);
        if (_engine is null || _engine.Snapshot.MediaPhase is MediaPhase.Empty or MediaPhase.Failed)
        {
            ChannelsText.Text = "\u2014";
            ChannelsButton.Visibility = Visibility.Collapsed;
            return;
        }

        string? label;
        if (_spdifActive)
        {
            // spdif frames report the IEC61937 carrier layout (2 ch for AC3, 8 for
            // TrueHD), not what the receiver decodes, so "5.1 → 2.0" would mislead.
            label = _ui.AudioBitstream;
        }
        else
        {
            string? source = await _engine.GetPropertyStringAsync("audio-params/hr-channels").ConfigureAwait(true);
            string? sourceCount = await _engine.GetPropertyStringAsync("audio-params/channel-count").ConfigureAwait(true);
            string? output = await _engine.GetPropertyStringAsync("audio-out-params/hr-channels").ConfigureAwait(true);
            string? outputCount = await _engine.GetPropertyStringAsync("audio-out-params/channel-count").ConfigureAwait(true);
            label = ChannelLayouts.Describe(source, ParseInt(sourceCount), output, ParseInt(outputCount));
        }

        // Permanent while a file is loaded: "—" until the audio chain reports, or for
        // files without an audio track.
        ChannelsText.Text = label ?? "\u2014";
        ChannelsButton.Visibility = Visibility.Visible;
        Label(ChannelsButton, label is null ? _ui.Channels : _ui.Channels + "  " + label);
    }

    private DynamicRange _currentRange = DynamicRange.Sdr;

    /// <summary>
    /// Permanent "HDR" indicator next to the channel chip: lit for any HDR transfer
    /// or metadata (HDR10, HDR10+, HLG, Dolby Vision), dimmed for SDR, gone when
    /// nothing is loaded. The tooltip names the exact kind.
    /// </summary>
    private void RefreshHdrChip()
    {
        if (_engine is null || _engine.Snapshot.MediaPhase is MediaPhase.Empty or MediaPhase.Failed)
        {
            HdrChip.Visibility = Visibility.Collapsed;
            return;
        }

        bool lit = _currentRange != DynamicRange.Sdr;
        HdrChip.Opacity = lit ? 1 : 0.35;
        string kind = _ui.DynamicRange + "  " + FormatBadges.Describe(_currentRange);
        ToolTipService.SetToolTip(HdrChip, kind);
        SetName(HdrChip, kind);
        HdrChip.Visibility = Visibility.Visible;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : null;

    private void ShowChannelsFlyout()
    {
        MenuFlyout flyout = new() { Placement = FlyoutPlacementMode.Top };
        if (_spdifActive)
        {
            // Passthrough may be on while an AAC track still decodes locally; only a
            // track that is really bitstreamed has nothing to downmix.
            flyout.Items.Add(new MenuFlyoutItem { Text = _ui.ChannelsBitstream, IsEnabled = false });
            flyout.ShowAt(ChannelsButton);
            return;
        }

        (string Value, string Label)[] options =
        [
            (ChannelLayouts.FollowPolicy, _ui.ChannelsFollowPolicy),
            (ChannelLayouts.Source, _ui.ChannelsSource),
            ("stereo", _ui.ChannelsStereo),
            ("2.1", "2.1"),
            ("5.1", "5.1"),
            ("7.1", "7.1"),
        ];
        foreach ((string value, string label) in options)
        {
            RadioMenuFlyoutItem item = new()
            {
                Text = label,
                GroupName = "channels",
                IsChecked = string.Equals(_settings.AudioChannelsOverride, value, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += async (_, _) => await SelectChannelsAsync(value).ConfigureAwait(true);
            flyout.Items.Add(item);
        }

        flyout.ShowAt(ChannelsButton);
    }

    private async Task SelectChannelsAsync(string value)
    {
        if (_engine is null || string.Equals(_settings.AudioChannelsOverride, value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings = _settings with { AudioChannelsOverride = value };
        await ApplyPlaybackPolicyAsync().ConfigureAwait(true);
        // audio-channels triggers an AO reinit; give it a moment before reading back.
        await Task.Delay(350).ConfigureAwait(true);
        await RefreshChannelsAsync().ConfigureAwait(true);
        ShowOsd(string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.ChannelsOsd, ChannelsText.Text));
    }

    private int _badgeTicket;

    /// <summary>
    /// Format chips after a load: video-params and the track list on a cloud strm
    /// are often still empty at FILE_LOADED, so poll until gamma / Dolby Vision /
    /// audio appear (or a few seconds elapse). Also applies Windows HDR and
    /// selects a late audio track from here, so those wait for the same probe.
    /// </summary>
    private async Task ShowFormatBadgesAsync()
    {
        int ticket = Interlocked.Increment(ref _badgeTicket);
        // Previous file's chips must not linger next to the new title while this
        // load's parameters are still being read.
        TitleBadges.Children.Clear();
        _currentBadges = [];
        _currentRange = DynamicRange.Sdr;
        if (_engine is null)
        {
            return;
        }

        // video-params and track-list on a cloud strm often arrive after FILE_LOADED.
        // Six tries (1.5 s) was enough for a local file and too short for OpenList 302.
        string? gamma = null;
        IReadOnlyList<TrackInfo> tracks = [];
        for (int attempt = 0; attempt < 24; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(250).ConfigureAwait(true);
            }

            if (ticket != _badgeTicket || _engine is null)
            {
                return;
            }

            gamma = await _engine.GetPropertyStringAsync("video-params/gamma").ConfigureAwait(true);
            tracks = TrackListParser.Parse(
                await _engine.GetPropertyStringAsync("track-list").ConfigureAwait(true));
            if (gamma is null && await _engine.GetPropertyStringAsync("vid").ConfigureAwait(true) is null or "no")
            {
                break; // audio-only
            }

            int? dv = tracks.FirstOrDefault(t => t.Type == "video")?.DolbyVisionProfile;
            bool hasAudio = tracks.Any(t => t.Type == "audio");
            if ((gamma is not null || HdrSource.IsSourceHdr(gamma, dv)) && hasAudio)
            {
                break;
            }
        }

        if (ticket != _badgeTicket || _engine is null)
        {
            return;
        }

        if (tracks.Any(t => t.Type == "audio")
            && tracks.All(t => t.Type != "audio" || !t.Selected))
        {
            Log.Information("Play: audio tracks present but none selected; setting aid=auto");
            await _engine.ExecuteCommandAsync(["set", "aid", "auto"]).ConfigureAwait(true);
            tracks = TrackListParser.Parse(
                await _engine.GetPropertyStringAsync("track-list").ConfigureAwait(true));
        }

        int? dolbyVision = tracks.FirstOrDefault(t => t.Type == "video")?.DolbyVisionProfile;
        await MaybeApplyWindowsHdrAsync(gamma, dolbyVision).ConfigureAwait(true);
        if (ticket != _badgeTicket || _engine is null)
        {
            return;
        }

        string? primaries = await _engine.GetPropertyStringAsync("video-params/primaries").ConfigureAwait(true);
        int? width = ParseInt(await _engine.GetPropertyStringAsync("video-params/w").ConfigureAwait(true));
        int? height = ParseInt(await _engine.GetPropertyStringAsync("video-params/h").ConfigureAwait(true));
        TrackInfo? video = tracks.FirstOrDefault(t => t.Type == "video" && t.Selected)
            ?? tracks.FirstOrDefault(t => t.Type == "video");
        TrackInfo? audio = tracks.FirstOrDefault(t => t.Type == "audio" && t.Selected);
        // mpv only publishes scene-max-* when the stream carries HDR10+ (ST 2094-40) metadata.
        bool hdr10Plus = false;
        foreach (string channel in (string[])["r", "g", "b"])
        {
            string? sceneMax = await _engine.GetPropertyStringAsync("video-params/scene-max-" + channel).ConfigureAwait(true);
            if (sceneMax is not null
                && double.TryParse(sceneMax, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double nits)
                && nits > 0)
            {
                hdr10Plus = true;
                break;
            }
        }

        MediaFormatInfo info = new(gamma, primaries, width, height, video, audio, hdr10Plus);
        IReadOnlyList<string> badges = FormatBadges.For(info);
        _currentBadges = badges;
        _currentRange = FormatBadges.RangeOf(info);
        await RefreshChannelsAsync().ConfigureAwait(true);
        if (ticket != _badgeTicket)
        {
            return;
        }

        Log.Information(
            "Play: format {Badges} range={Range} gamma={Gamma} primaries={Primaries} size={Width}x{Height} dv={DolbyVisionProfile} hdr10plus={Hdr10Plus} acodec={AudioCodec} profile={AudioProfile}",
            badges.Count == 0 ? "(none)" : string.Join(" | ", badges),
            _currentRange,
            gamma,
            primaries,
            width,
            height,
            video?.DolbyVisionProfile,
            hdr10Plus,
            audio?.Codec,
            audio?.CodecProfile);

        FormatBadgeStrip.Children.Clear();
        TitleBadges.Children.Clear();
        if (badges.Count == 0)
        {
            FormatBadgeStrip.Visibility = Visibility.Collapsed;
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            if (ticket == _badgeTicket)
            {
                await RefreshChannelsAsync().ConfigureAwait(true);
            }

            return;
        }

        foreach (string badge in badges)
        {
            FormatBadgeStrip.Children.Add(FormatBadgeVisuals.CreateChip(badge));
            // The compact copy stays in the title bar so the format is still
            // visible after the large chips have faded.
            TitleBadges.Children.Add(FormatBadgeVisuals.CreateTitleChip(badge));
        }

        string summary = _ui.FormatBadgesTitle + ": " + string.Join(", ", badges);
        SetName(FormatBadgeStrip, summary);
        SetName(TitleBadges, summary);
        FormatBadgeStrip.Opacity = 1;
        FormatBadgeStrip.Visibility = Visibility.Visible;
        // The AO reports its output layout a little after the first frames; refresh
        // the transport-bar chip once more so "7.1 → 5.1" is not missed.
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        if (ticket != _badgeTicket)
        {
            return;
        }

        await RefreshChannelsAsync().ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(true);
        if (ticket == _badgeTicket)
        {
            FadeOut(FormatBadgeStrip);
        }
    }

    private IReadOnlyList<string> _currentBadges = [];

    private async Task ApplyPlaybackPolicyAsync(bool save = true)
    {
        if (_engine is null)
        {
            return;
        }

        bool passthrough = _settings.AudioPassthrough;
        if (passthrough)
        {
            _night = false;
        }

        PlaybackPolicyOptions policy = new PlaybackPolicyOptions
        {
            SubAssOverride = string.IsNullOrWhiteSpace(_settings.SubAssOverride)
                ? "no"
                : _settings.SubAssOverride,
            AudioDevice = string.IsNullOrWhiteSpace(_settings.AudioDevice) ? null : _settings.AudioDevice,
        }
            .WithAudioPolicy(_audioPolicy)
            .WithPassthrough(passthrough)
            .WithNightMode(_night);
        // User downmix from the transport bar overrides the policy's layout. mpv only
        // applies audio-channels to PCM, so a bitstreamed track is unaffected.
        if (!string.IsNullOrEmpty(_settings.AudioChannelsOverride)
            && ChannelLayouts.IsValidOverride(_settings.AudioChannelsOverride))
        {
            policy = policy with { AudioChannels = _settings.AudioChannelsOverride };
        }

        await _engine.ApplyPropertiesAsync(policy.ToProperties()).ConfigureAwait(true);
        if (save)
        {
            await SaveSettingsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Whether the current track really leaves as spdif, and whether a passthrough
    /// codec unexpectedly came out as PCM (an AAC track decoding locally is not a
    /// fallback). Runs from <see cref="RefreshChannelsAsync"/>, so loads, track
    /// switches and policy changes all pass through here.
    /// </summary>
    private async Task RefreshPassthroughStateAsync()
    {
        bool spdif = false;
        bool fallback = false;
        string? format = null;
        if (_engine is not null
            && _engine.Snapshot.MediaPhase is not (MediaPhase.Empty or MediaPhase.Opening or MediaPhase.Failed))
        {
            format = await _engine.GetPropertyStringAsync("audio-out-params/format").ConfigureAwait(true);
            spdif = AudioPassthrough.IsSpdifFormat(format);
            if (_settings.AudioPassthrough && !spdif && !string.IsNullOrWhiteSpace(format))
            {
                string? codec = await _engine.GetPropertyStringAsync("audio-codec-name").ConfigureAwait(true);
                fallback = AudioPassthrough.IsPassthroughCodec(codec);
            }
        }

        _spdifActive = spdif;
        if (fallback == _bitstreamFallback)
        {
            // Called again after the AO settles; re-opening the banner would flicker.
            return;
        }

        _bitstreamFallback = fallback;
        if (!fallback)
        {
            HintBanner.IsOpen = false;
            return;
        }

        HintBanner.Message = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            _ui.BitstreamFallback,
            format);
        SetName(HintBanner, HintBanner.Message);
        HintBanner.IsOpen = true;
    }

    private async Task ToggleFullscreenAsync()
    {
        if (IsPip())
        {
            await LeavePipAsync().ConfigureAwait(true);
            _wantFullscreen = true;
            await EnterWinUiFullscreenAsync().ConfigureAwait(true);
            return;
        }

        if (_surface is { IsTopLevel: true } || IsWinUiFullscreen())
        {
            await LeaveAnyFullscreenAsync().ConfigureAwait(true);
            return;
        }

        _wantFullscreen = true;
        await EnterWinUiFullscreenAsync().ConfigureAwait(true);
    }

    private bool IsWinUiFullscreen() =>
        _appWindow?.Presenter.Kind == AppWindowPresenterKind.FullScreen || _borderlessMaximize;

    private async Task EnterWinUiFullscreenAsync()
    {
        if (_surface is { IsTopLevel: true } || _appWindow is null || IsWinUiFullscreen())
        {
            return;
        }

        _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        if (_appWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
        {
            _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (_appWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.SetBorderAndTitleBar(false, false);
                overlapped.Maximize();
                _borderlessMaximize = true;
            }
        }

        SetFullscreenIcon(true);
        ShowChrome();
        await Task.Delay(200).ConfigureAwait(true);
        if (_surface is not null && !_surface.IsTopLevel)
        {
            await _surface.ResizeAsync(_surface.ReadLayout()).ConfigureAwait(true);
        }

        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task LeaveWinUiFullscreenAsync()
    {
        if (_appWindow is null || !IsWinUiFullscreen())
        {
            return;
        }

        if (_borderlessMaximize && _appWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.SetBorderAndTitleBar(true, true);
            overlapped.Restore();
            _borderlessMaximize = false;
        }
        else
        {
            _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        }

        SetFullscreenIcon(false);
        ShowChrome();
        await Task.Delay(200).ConfigureAwait(true);
        if (_surface is not null && !_surface.IsTopLevel)
        {
            await _surface.ResizeAsync(_surface.ReadLayout()).ConfigureAwait(true);
        }

        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async void OnTopLevelLeaveRequested(object? sender, EventArgs e)
    {
        if (_surface is not { IsTopLevel: true })
        {
            return;
        }

        await LeaveAnyFullscreenAsync().ConfigureAwait(true);
    }

    private Task ToggleTopLevelAsync() => ToggleFullscreenAsync();

    private async Task LeaveTopLevelAsync()
    {
        if (_surface is not { IsTopLevel: true })
        {
            return;
        }

        await _surface.LeaveTopLevelAsync(DisplayIsAdvancedColor()).ConfigureAwait(true);
        _topLevelAutomatic = false;
        ApplyTopLevelChrome(false);
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task LeaveAnyFullscreenAsync()
    {
        _wantFullscreen = false;
        if (_surface is { IsTopLevel: true })
        {
            await LeaveTopLevelAsync().ConfigureAwait(true);
            return;
        }

        if (IsWinUiFullscreen())
        {
            await LeaveWinUiFullscreenAsync().ConfigureAwait(true);
        }

        if (IsPip())
        {
            await LeavePipAsync().ConfigureAwait(true);
        }
    }

    private bool IsPip() =>
        _pipMini || _appWindow?.Presenter.Kind == AppWindowPresenterKind.CompactOverlay;

    private async Task TogglePipAsync()
    {
        if (IsPip())
        {
            await LeavePipAsync().ConfigureAwait(true);
            return;
        }

        await EnterPipAsync().ConfigureAwait(true);
    }

    private async Task EnterPipAsync()
    {
        if (_appWindow is null || IsPip())
        {
            return;
        }

        _wantFullscreen = false;
        if (_surface is { IsTopLevel: true })
        {
            await LeaveTopLevelAsync().ConfigureAwait(true);
        }

        if (IsWinUiFullscreen())
        {
            await LeaveWinUiFullscreenAsync().ConfigureAwait(true);
        }

        _savedPipPosition = _appWindow.Position;
        _savedPipSize = _appWindow.Size;
        _savedPipBounds = true;
        VideoParams? video = _engine?.Snapshot.VideoParams;
        (int width, int height) = PipLayout.SizeFor(video?.Width ?? 0, video?.Height ?? 0);
        ExtendsContentIntoTitleBar = false;
        _appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
        if (_appWindow.Presenter.Kind != AppWindowPresenterKind.CompactOverlay)
        {
            _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (_appWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.IsAlwaysOnTop = true;
            }

            _pipMini = true;
        }

        _appWindow.Resize(new SizeInt32(width, height));
        PlaylistPane.Visibility = Visibility.Collapsed;
        HideEmbyPage();
        ShowChrome();
        ShowOsd(_ui.PictureInPicture);
        await Task.Delay(200).ConfigureAwait(true);
        if (_surface is not null && !_surface.IsTopLevel)
        {
            await _surface.ResizeAsync(_surface.ReadLayout()).ConfigureAwait(true);
        }

        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task LeavePipAsync()
    {
        if (_appWindow is null || !IsPip())
        {
            return;
        }

        if (_appWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.IsAlwaysOnTop = false;
        }

        _pipMini = false;
        _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (_savedPipBounds)
        {
            _appWindow.Move(_savedPipPosition);
            _appWindow.Resize(_savedPipSize);
            _savedPipBounds = false;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDrag);
        ApplyCaptionColors();
        ShowChrome();
        await Task.Delay(200).ConfigureAwait(true);
        if (_surface is not null && !_surface.IsTopLevel)
        {
            await _surface.ResizeAsync(_surface.ReadLayout()).ConfigureAwait(true);
        }

        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task MaybeApplyFullscreenAfterLoadAsync()
    {
        if (!_wantFullscreen || _surface is null)
        {
            return;
        }

        if (_surface.IsTopLevel)
        {
            await LeaveTopLevelAsync().ConfigureAwait(true);
        }

        if (!IsWinUiFullscreen())
        {
            await EnterWinUiFullscreenAsync().ConfigureAwait(true);
        }
    }

    private bool DisplayIsAdvancedColor() =>
        _surface?.CurrentDisplay?.IsAdvancedColor
        ?? WindowsAdvancedColor.ForWindow(_hwnd)?.IsAdvancedColor
        ?? WindowsAdvancedColor.AnyHdrEnabled();

    private void ApplyTopLevelChrome(bool topLevel)
    {
        FallbackBanner.Message = _topLevelAutomatic ? _ui.FallbackHdrBanner : _ui.FallbackBanner;
        FallbackBanner.IsOpen = topLevel;
        if (topLevel)
        {
            SetChromeVisible(false, immediate: true);
            MiniProgress.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShowChrome();
        }

        SetFullscreenIcon(topLevel);
    }

    private async Task ToggleInfoOverlayAsync()
    {
        if (!IsPlaybackWindow())
        {
            InfoOverlay.Visibility = Visibility.Collapsed;
            _infoRefreshTimer?.Stop();
            return;
        }

        bool show = InfoOverlay.Visibility != Visibility.Visible;
        InfoOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            // One immediate refresh, then 1 Hz polling for bitrate/fps while the
            // overlay stays open. The timer stops on hide so we do not poll mpv
            // at idle.
            await RefreshStatusAsync().ConfigureAwait(true);
            _infoRefreshTimer?.Start();
        }
        else
        {
            _infoRefreshTimer?.Stop();
        }
    }

    /// <summary>
    /// Technical state for the info overlay. Only runs when the overlay is
    /// visible; the timer fires this once a second so bitrate / fps stay live
    /// without an mpv observer.
    /// </summary>
    private async Task RefreshStatusAsync()
    {
        if (_engine is null || InfoOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        // --- Output section (unchanged from before the rewrite) ---
        string hwdec = await _engine.GetPropertyStringAsync("hwdec-current").ConfigureAwait(true) ?? "-";
        string vo = await _engine.GetPropertyStringAsync("current-vo").ConfigureAwait(true) ?? "-";
        string fmt = await _engine.GetPropertyStringAsync("d3d11-output-format").ConfigureAwait(true) ?? "-";
        string csp = await _engine.GetPropertyStringAsync("d3d11-output-csp").ConfigureAwait(true) ?? "-";
        string peak = await _engine.GetPropertyStringAsync("target-peak").ConfigureAwait(true) ?? "-";
        string pix = await _engine.GetPropertyStringAsync("video-params/pixelformat").ConfigureAwait(true) ?? "-";
        string gamma = await _engine.GetPropertyStringAsync("video-params/gamma").ConfigureAwait(true) ?? "-";
        string size = await _engine.GetPropertyStringAsync("video-params/w").ConfigureAwait(true) is { } w
            && await _engine.GetPropertyStringAsync("video-params/h").ConfigureAwait(true) is { } h
            ? $"{w}×{h}"
            : "-";
        string containerFpsRaw = await _engine.GetPropertyStringAsync("container-fps").ConfigureAwait(true) ?? "";
        string effectiveFpsRaw = await _engine.GetPropertyStringAsync("estimated-vf-fps").ConfigureAwait(true) ?? "";
        // video-format is the short codec id (h264, hevc); video-codec is the long libavcodec description.
        string vcodec = await _engine.GetPropertyStringAsync("video-codec").ConfigureAwait(true)
            ?? await _engine.GetPropertyStringAsync("video-format").ConfigureAwait(true)
            ?? "-";
        string acodec = await _engine.GetPropertyStringAsync("audio-codec-name").ConfigureAwait(true) ?? "-";
        string channels = await _engine.GetPropertyStringAsync("audio-params/channels").ConfigureAwait(true) ?? "-";
        string channelCountRaw = await _engine.GetPropertyStringAsync("audio-params/channel-count").ConfigureAwait(true) ?? "";
        string sampleRateRaw = await _engine.GetPropertyStringAsync("audio-params/samplerate").ConfigureAwait(true) ?? "";
        string videoBitrateRaw = await _engine.GetPropertyStringAsync("video-bitrate").ConfigureAwait(true) ?? "";
        string audioBitrateRaw = await _engine.GetPropertyStringAsync("audio-bitrate").ConfigureAwait(true) ?? "";
        string containerRaw = await _engine.GetPropertyStringAsync("file-format").ConfigureAwait(true) ?? "";
        string fileSizeRaw = await _engine.GetPropertyStringAsync("file-size").ConfigureAwait(true) ?? "";
        string device = await _engine.GetPropertyStringAsync("audio-device").ConfigureAwait(true) ?? "auto";

        // The audio codec profile comes from the selected audio track in the
        // track-list. RefreshStatusAsync already runs only when the overlay is
        // visible, so the parse cost is fine.
        string acodecProfile = "";
        try
        {
            string? tracksJson = await _engine.GetPropertyStringAsync("track-list").ConfigureAwait(true);
            TrackInfo? audio = TrackListParser.Parse(tracksJson)
                .FirstOrDefault(t => t.Type == "audio" && t.Selected);
            acodecProfile = audio?.CodecProfile ?? "";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "track-list parse failed in info overlay");
        }

        // --- Header ---
        string name = _currentUri is null ? "-" : Path.GetFileName(Uri.UnescapeDataString(_currentUri.AbsolutePath));
        string playlist = _playlist.Count > 1 ? $"  ({_playlistIndex + 1}/{_playlist.Count})" : "";
        string badges = _currentBadges.Count > 0 ? "  [" + string.Join(" · ", _currentBadges) + "]" : "";
        string mode = _surface is { IsTopLevel: true }
            ? _ui.StatusTopLevel
            : IsPip() ? _ui.StatusPip
            : IsWinUiFullscreen() ? _ui.StatusFullscreen : _ui.StatusWindow;
        string refresh = _surface?.CurrentRefreshRateHz is > 0 and var hz
            ? hz.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " Hz"
            : "-";
        string passthrough = _settings.AudioPassthrough
            ? "  " + (_bitstreamFallback ? _ui.AudioBitstreamPcm : _spdifActive ? _ui.AudioBitstream : _ui.AudioPassthrough)
            : "";
        string flags = passthrough + (_night ? "  " + _ui.Night : "") + (_muted ? "  " + _ui.Mute : "");

        // Local helpers must be declared before use (C# requirement).
        string FormatFpsForDisplay(string containerFps, string effectiveFps)
        {
            bool hasContainer = double.TryParse(containerFps, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cFps) && cFps > 0;
            bool hasEffective = double.TryParse(effectiveFps, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double eFps) && eFps > 0;
            if (!hasContainer && !hasEffective)
            {
                return _ui.InfoValueDash;
            }

            Func<double, string> fpsFmt = v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            if (hasContainer && hasEffective && Math.Abs(cFps - eFps) > 0.01d)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.InfoFpsWithEffective, fpsFmt(cFps), fpsFmt(eFps));
            }

            return fpsFmt(hasEffective ? eFps : cFps) + " fps";
        }

        string ReportingMethodLabel()
        {
            return _currentSourceKind switch
            {
                MediaSourceKind.ServerDirectPlay => "DirectPlay",
                MediaSourceKind.ServerTranscode => "Transcode",
                _ => "",
            };
        }

        // --- Section 1: Playback kind ---
        string kindLabel = _currentSourceKind switch
        {
            MediaSourceKind.LocalFile => _ui.InfoKindLocalFile,
            MediaSourceKind.LocalDisc => _ui.InfoKindLocalDisc,
            MediaSourceKind.NetworkShare => _ui.InfoKindNetworkShare,
            MediaSourceKind.StrmDirect => _ui.InfoKindStrmDirect,
            MediaSourceKind.StrmRelay => _ui.InfoKindStrmRelay,
            MediaSourceKind.ServerDirectPlay => _ui.InfoKindServerDirect,
            MediaSourceKind.ServerTranscode => _ui.InfoKindServerTranscode,
            _ => _ui.InfoKindUnknown,
        };
        string methodLabel = ReportingMethodLabel();
        string playbackSection =
            $"{_ui.InfoPlayback}\n  {kindLabel}{(string.IsNullOrEmpty(methodLabel) ? "" : "  (" + methodLabel + ")")}";

        // --- Section 2: Media source ---
        string container = InfoFormatters.ContainerLabel(containerRaw);
        // file-size is only meaningful for local-style sources; mpv reports
        // nothing useful for live / transcoded streams.
        bool localish = _currentSourceKind is MediaSourceKind.LocalFile
            or MediaSourceKind.LocalDisc
            or MediaSourceKind.NetworkShare
            or MediaSourceKind.StrmDirect;
        long? fileSize = localish && long.TryParse(fileSizeRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long bytes)
            ? bytes
            : null;
        string sizeLabel = localish ? InfoFormatters.FormatBytes(fileSize, _ui.InfoValueDash) : _ui.InfoValueDash;
        string sourceLine1 = localish
            ? $"  {_ui.InfoLabelContainer}: {container}  {_ui.InfoLabelSize}: {sizeLabel}"
            : $"  {_ui.InfoLabelContainer}: {container}";
        string uriDisplay = _currentUri is null ? "-" : _currentUri.AbsoluteUri;
        string sourceSection =
            $"{_ui.InfoSource}\n{sourceLine1}\n  {_ui.InfoLabelUri}: {uriDisplay}";

        // --- Section 3: Video ---
        string rangeLabel = _ui.InfoRangeSdr;
        switch (_currentRange)
        {
            case DynamicRange.Hdr10: rangeLabel = _ui.InfoRangeHdr10; break;
            case DynamicRange.Hdr10Plus: rangeLabel = _ui.InfoRangeHdr10Plus; break;
            case DynamicRange.Hlg: rangeLabel = _ui.InfoRangeHlg; break;
            case DynamicRange.DolbyVision: rangeLabel = _ui.InfoRangeDolbyVision; break;
        }
        string fps = FormatFpsForDisplay(containerFpsRaw, effectiveFpsRaw);
        long? videoBitrate = long.TryParse(videoBitrateRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long vbps) ? vbps : null;
        string videoBitrateLabel = InfoFormatters.FormatBitrate(videoBitrate, _ui.InfoValueDash);
        string videoLine1 = $"  {_ui.InfoLabelCodec}: {vcodec}  {_ui.InfoLabelRange}: {rangeLabel}";
        string videoLine2 = $"  {_ui.InfoLabelResolution}: {size}  {_ui.InfoLabelFps}: {fps}  {_ui.InfoLabelBitrate}: {videoBitrateLabel}";
        string videoLine3 = $"  pixel={pix}  gamma={gamma}  hwdec={hwdec}";
        string videoSection = $"{_ui.InfoVideo}\n{videoLine1}\n{videoLine2}\n{videoLine3}";

        // --- Section 4: Audio ---
        string acodecLabel = FormatBadges.CodecProfileLabel(acodec, acodecProfile);
        string channelCountLabel = int.TryParse(channelCountRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int chCount) && chCount > 0
            ? chCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : _ui.InfoValueDash;
        string sampleRateLabel = double.TryParse(sampleRateRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double srHz) && srHz > 0
            ? (srHz / 1000d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz"
            : _ui.InfoValueDash;
        long? audioBitrate = long.TryParse(audioBitrateRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long abps) ? abps : null;
        string audioBitrateLabel = InfoFormatters.FormatBitrate(audioBitrate, _ui.InfoValueDash);
        string audioLine1 = $"  {_ui.InfoLabelCodec}: {acodecLabel}  {_ui.InfoLabelChannels}: {channelCountLabel} ({channels})";
        string audioLine2 = $"  {_ui.InfoLabelSampleRate}: {sampleRateLabel}  {_ui.InfoLabelBitrate}: {audioBitrateLabel}";
        string audioLine3 = $"  {AudioPolicyLabel(_audioPolicy)}  {device}{flags}";
        string audioSection = $"{_ui.InfoAudio}\n{audioLine1}\n{audioLine2}\n{audioLine3}";

        // --- Section 5: Output (kept at the bottom so we don't drop anything) ---
        string outputLine1 = $"  {fmt} / {csp}  peak={peak}  vo={vo}";
        string outputLine2 = $"  {mode}  {refresh}";
        string outputSection = $"{_ui.InfoOutput}\n{outputLine1}\n{outputLine2}";

        StatusText.Text =
            $"{name}{playlist}{badges}\n" +
            $"\n{playbackSection}\n" +
            $"\n{sourceSection}\n" +
            $"\n{videoSection}\n" +
            $"\n{audioSection}\n" +
            $"\n{outputSection}";
    }

    private async Task SaveProgressAsync(bool? paused = null)
    {
        if (_engine is null || _store is null || _currentUri is null || _progressKey is null)
        {
            return;
        }

        TimeSpan? position = _engine.Snapshot.Position;
        if (position is null)
        {
            return;
        }

        long positionMs = (long)position.Value.TotalMilliseconds;
        long? durationMs = (long?)_engine.Snapshot.Duration?.TotalMilliseconds;
        if (PlaybackResume.IsNearEnd(positionMs, durationMs))
        {
            positionMs = 0;
        }

        await _store.UpsertProgressAsync(new PlaybackProgressRecord(
            _progressKey,
            positionMs,
            durationMs,
            DateTimeOffset.UtcNow)).ConfigureAwait(true);
        if (_session is not null && _reporting is not null)
        {
            bool isPaused = paused ?? _engine.Snapshot.PlaybackIntent == PlaybackIntent.Paused;
            try
            {
                // Reporting is best effort: a flaky server must not turn into a
                // playback error for the user.
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(8));
                if (isPaused)
                {
                    await _session.PauseAsync(
                            _reporting,
                            position.Value,
                            _engine.Snapshot.PlaybackGeneration,
                            timeout.Token)
                        .ConfigureAwait(true);
                }
                else
                {
                    await _session.ProgressAsync(
                            _reporting,
                            position.Value,
                            _engine.Snapshot.PlaybackGeneration,
                            timeout.Token)
                        .ConfigureAwait(true);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
            {
                Log.Warning(ex, "Emby progress report failed");
            }
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (_store is null)
        {
            return;
        }

        _settings = _settings with
        {
            Volume = VolumeSlider.Value,
            Mute = _muted,
            NightMode = _night,
            AudioPolicy = _audioPolicy,
            Quality = _settings.Quality,
            Speed = _speed,
            SubDelaySeconds = _subDelay,
            Language = _settings.Language,
            MatchDisplayRefresh = _settings.MatchDisplayRefresh,
            AutoEnableWindowsHdr = _settings.AutoEnableWindowsHdr,
            SeekThumbnails = _settings.SeekThumbnails,
            GamepadEnabled = _settings.GamepadEnabled,
            AllowServerFilePaths = _settings.AllowServerFilePaths,
            AudioChannelsOverride = _settings.AudioChannelsOverride,
            FullscreenProgressLine = _settings.FullscreenProgressLine,
        };
        string json = SimpleSettingsSerializer.ToJson(_settings);
        if (string.Equals(json, _lastSavedSettingsJson, StringComparison.Ordinal))
        {
            // The 10 s timer calls this unconditionally; skip the write when nothing changed.
            return;
        }

        await _store.SetSettingAsync(SimpleSettingsSerializer.StoreKey, json).ConfigureAwait(true);
        _lastSavedSettingsJson = json;
    }

    private async Task ExportDiagnosticsAsync()
    {
        if (_diagnostics is null)
        {
            return;
        }

        string path = await _diagnostics.ExportAsync().ConfigureAwait(true);
        ShowOsd(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            _ui.DiagnosticsExported,
            path));
    }

    private void ShowError(string message)
    {
        ErrorPanel.Title = _ui.CannotPlay;
        ErrorPanel.Message = message;
        SetName(ErrorPanel, _ui.CannotPlay + ": " + message);
        ErrorPanel.IsOpen = true;
        ShowChrome();
    }

    private void HideError() => ErrorPanel.IsOpen = false;

    private void ReplacePlaylistFromInput(string input)
    {
        string trimmed = input.Trim().Trim('"');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            _playlist.Clear();
            _playlist.Add(trimmed);
            _playlistIndex = 0;
            return;
        }

        if (LocalPlaybackFactory.ResolveDiscRoot(trimmed) is { } disc)
        {
            _playlist.Clear();
            _playlist.Add(disc);
            _playlistIndex = 0;
            return;
        }

        if (Directory.Exists(trimmed))
        {
            _playlist.Clear();
            _playlist.AddRange(LocalPlaybackFactory.EnumerateMediaFiles(trimmed));
            _playlistIndex = _playlist.Count == 0 ? -1 : 0;
            return;
        }

        if (!File.Exists(trimmed) || LocalPlaybackFactory.IsSubtitlePath(trimmed))
        {
            return;
        }

        string full = Path.GetFullPath(trimmed);
        string? directory = Path.GetDirectoryName(full);
        _playlist.Clear();
        if (directory is null)
        {
            _playlist.Add(full);
            _playlistIndex = 0;
            return;
        }

        IReadOnlyList<string> files = LocalPlaybackFactory.EnumerateMediaFiles(directory);
        if (files.Count == 0)
        {
            _playlist.Add(full);
            _playlistIndex = 0;
            return;
        }

        _playlist.AddRange(files);
        int index = LocalPlaybackFactory.IndexOfPath(_playlist, full);
        if (index < 0)
        {
            _playlist.Insert(0, full);
            _playlistIndex = 0;
        }
        else
        {
            _playlistIndex = index;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e) =>
        e.AcceptedOperation = DataPackageOperation.Copy;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
        if (items.Count == 0)
        {
            return;
        }

        if (items[0] is StorageFolder folder)
        {
            await PlayPathAsync(folder.Path).ConfigureAwait(true);
            return;
        }

        if (items[0] is StorageFile file)
        {
            await PlayPathAsync(file.Path).ConfigureAwait(true);
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowChrome();
        RestartHideTimer();
    }

    private void OnChromePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _chromePinned = true;
        ShowChrome();
        _hideTimer.Stop();
    }

    private void OnChromePointerExited(object sender, PointerRoutedEventArgs e)
    {
        _chromePinned = false;
        RestartHideTimer();
    }

    private void ShowChrome()
    {
        if (_surface is { IsTopLevel: true } || IsPip())
        {
            if (IsPip())
            {
                SetChromeVisible(false, immediate: true);
                MiniProgress.Visibility = Visibility.Visible;
            }

            return;
        }

        SetChromeVisible(true);
        MiniProgress.Visibility = Visibility.Collapsed;
    }

    private bool _chromeVisible = true;

    /// <summary>
    /// Fades the top and transport bars (opacity + a short slide). Elements stay
    /// in the tree so layout never jumps; hit testing is switched off while hidden.
    /// Reduce-motion falls back to an instant switch.
    /// </summary>
    private void SetChromeVisible(bool visible, bool immediate = false)
    {
        if (_chromeVisible == visible && !immediate)
        {
            return;
        }

        _chromeVisible = visible;
        TopBar.IsHitTestVisible = visible;
        TransportBar.IsHitTestVisible = visible;
        if (immediate || _reduceMotion)
        {
            TopBar.Opacity = visible ? 1 : 0;
            TransportBar.Opacity = visible ? 1 : 0;
            Slide(TopBar).Y = visible ? 0 : -12;
            Slide(TransportBar).Y = visible ? 0 : 12;
            return;
        }

        Animate(TopBar, visible, -12);
        Animate(TransportBar, visible, 12);
    }

    private static TranslateTransform Slide(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform existing)
        {
            return existing;
        }

        TranslateTransform transform = new();
        element.RenderTransform = transform;
        return transform;
    }

    private static void Animate(UIElement element, bool visible, double hiddenOffset)
    {
        TranslateTransform transform = Slide(element);
        Duration duration = new(TimeSpan.FromMilliseconds(visible ? 160 : 220));
        Microsoft.UI.Xaml.Media.Animation.Storyboard storyboard = new();
        Microsoft.UI.Xaml.Media.Animation.DoubleAnimation fade = new()
        {
            To = visible ? 1 : 0,
            Duration = duration,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, element);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        Microsoft.UI.Xaml.Media.Animation.DoubleAnimation slide = new()
        {
            To = visible ? 0 : hiddenOffset,
            Duration = duration,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slide, transform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slide, "Y");
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

    private void FadeOut(UIElement element)
    {
        if (_reduceMotion)
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        Microsoft.UI.Xaml.Media.Animation.Storyboard storyboard = new();
        Microsoft.UI.Xaml.Media.Animation.DoubleAnimation fade = new()
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, element);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);
        storyboard.Completed += (_, _) =>
        {
            if (element.Opacity <= 0.01)
            {
                element.Visibility = Visibility.Collapsed;
                element.Opacity = 1;
            }
        };
        storyboard.Begin();
    }

    private void HideChrome()
    {
        if (_chromePinned
            || _currentUri is null
            || _surface is { IsTopLevel: true }
            || IsPip()
            || PlaylistPane.Visibility == Visibility.Visible
            || EmbyPage.Visibility == Visibility.Visible
            || !AccessibilityPolicy.ShouldAutoHideChrome(_reduceMotion, _highContrast))
        {
            return;
        }

        SetChromeVisible(false);
        // Fullscreen is meant to be the picture alone; the thin progress line stays
        // only when the user asks for it. Windowed mode keeps it as a position cue.
        bool fullscreen = IsWinUiFullscreen() || _surface is { IsTopLevel: true };
        MiniProgress.Visibility = fullscreen && !_settings.FullscreenProgressLine
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void RestartHideTimer()
    {
        _hideTimer.Stop();
        if (_currentUri is null
            || _chromePinned
            || _surface is { IsTopLevel: true }
            || !AccessibilityPolicy.ShouldAutoHideChrome(_reduceMotion, _highContrast))
        {
            return;
        }

        _hideTimer.Start();
    }

    private async Task PollGamepadAsync()
    {
        if (!_settings.GamepadEnabled)
        {
            _padWas = PadFace.None;
            return;
        }

        try
        {
            if (Gamepad.Gamepads.Count == 0)
            {
                _padWas = PadFace.None;
                return;
            }

            GamepadReading reading = Gamepad.Gamepads[0].GetCurrentReading();
            PadFace now = FromReading(reading);
            IReadOnlyList<ShortcutCommand> commands = PadMap.Rising(now, _padWas);
            _padWas = now;
            if (commands.Count == 0)
            {
                return;
            }

            ShowChrome();
            RestartHideTimer();
            foreach (ShortcutCommand command in commands)
            {
                await RunShortcutAsync(command).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Gamepad poll failed");
        }
    }

    private static PadFace FromReading(GamepadReading reading)
    {
        PadFace face = PadFace.None;
        GamepadButtons buttons = reading.Buttons;
        if (buttons.HasFlag(GamepadButtons.A))
        {
            face |= PadFace.A;
        }

        if (buttons.HasFlag(GamepadButtons.B))
        {
            face |= PadFace.B;
        }

        if (buttons.HasFlag(GamepadButtons.X))
        {
            face |= PadFace.X;
        }

        if (buttons.HasFlag(GamepadButtons.Y))
        {
            face |= PadFace.Y;
        }

        if (buttons.HasFlag(GamepadButtons.DPadUp) || reading.LeftThumbstickY >= 0.65)
        {
            face |= PadFace.DPadUp;
        }

        if (buttons.HasFlag(GamepadButtons.DPadDown) || reading.LeftThumbstickY <= -0.65)
        {
            face |= PadFace.DPadDown;
        }

        if (buttons.HasFlag(GamepadButtons.DPadLeft) || reading.LeftThumbstickX <= -0.65)
        {
            face |= PadFace.DPadLeft;
        }

        if (buttons.HasFlag(GamepadButtons.DPadRight) || reading.LeftThumbstickX >= 0.65)
        {
            face |= PadFace.DPadRight;
        }

        if (buttons.HasFlag(GamepadButtons.LeftShoulder))
        {
            face |= PadFace.LeftShoulder;
        }

        if (buttons.HasFlag(GamepadButtons.RightShoulder))
        {
            face |= PadFace.RightShoulder;
        }

        if (buttons.HasFlag(GamepadButtons.Menu))
        {
            face |= PadFace.Menu;
        }

        if (buttons.HasFlag(GamepadButtons.View))
        {
            face |= PadFace.View;
        }

        return face;
    }

    private async Task RunShortcutAsync(ShortcutCommand command)
    {
        switch (command)
        {
            case ShortcutCommand.PlayPause:
                await TogglePauseAsync().ConfigureAwait(true);
                break;
            case ShortcutCommand.Exit:
                await LeaveAnyFullscreenAsync().ConfigureAwait(true);
                break;
            case ShortcutCommand.ToggleFullscreen:
                await ToggleFullscreenAsync().ConfigureAwait(true);
                break;
            case ShortcutCommand.CycleAudioTrack:
                await CycleAsync("aid").ConfigureAwait(true);
                break;
            case ShortcutCommand.CycleSubtitleTrack:
                await CycleAsync("sid").ConfigureAwait(true);
                break;
            case ShortcutCommand.VolumeUp:
                VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
                break;
            case ShortcutCommand.VolumeDown:
                VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
                break;
            case ShortcutCommand.ToggleMute:
                await ToggleMuteAsync().ConfigureAwait(true);
                break;
            case ShortcutCommand.SeekBackwardSmall:
                await SeekByAsync(-5).ConfigureAwait(true);
                break;
            case ShortcutCommand.SeekForwardSmall:
                await SeekByAsync(5).ConfigureAwait(true);
                break;
            case ShortcutCommand.SeekBackwardLarge:
                await PlayRelativeAsync(-1).ConfigureAwait(true);
                break;
            case ShortcutCommand.SeekForwardLarge:
                await PlayRelativeAsync(1).ConfigureAwait(true);
                break;
        }
    }

    private async Task SeekByAsync(double seconds)
    {
        if (_engine is null)
        {
            return;
        }

        TimeSpan pos = (_engine.Snapshot.Position ?? TimeSpan.Zero) + TimeSpan.FromSeconds(seconds);
        if (pos < TimeSpan.Zero)
        {
            pos = TimeSpan.Zero;
        }

        if (_engine.Snapshot.Duration is { } duration && pos > duration)
        {
            pos = duration;
        }

        await _engine.SeekAsync(pos).ConfigureAwait(true);
        ShowOsd(seconds < 0 ? _ui.SeekBack : _ui.SeekForward);
    }

    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        ShowChrome();
        RestartHideTimer();
        if (HasModifier())
        {
            // Ctrl+, opens settings (the Windows convention) and Ctrl+- / Ctrl+= (or the
            // numpad - / +) shift the sound against the picture, as in mpv. Other
            // Ctrl/Alt/Win combinations are not player shortcuts (Ctrl+A in a text box
            // must not cycle audio tracks).
            if (IsKeyDown(Windows.System.VirtualKey.Control))
            {
                switch (e.Key)
                {
                    case (Windows.System.VirtualKey)0xBC:
                        e.Handled = true;
                        OpenSettings();
                        break;
                    case (Windows.System.VirtualKey)0xBD:
                    case Windows.System.VirtualKey.Subtract:
                        e.Handled = true;
                        await NudgeAudioDelayAsync(-AudioDelay.Step).ConfigureAwait(true);
                        break;
                    case (Windows.System.VirtualKey)0xBB:
                    case Windows.System.VirtualKey.Add:
                        e.Handled = true;
                        await NudgeAudioDelayAsync(AudioDelay.Step).ConfigureAwait(true);
                        break;
                }
            }

            return;
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.F:
                e.Handled = true;
                await ToggleFullscreenAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.T:
                e.Handled = true;
                await ToggleTopLevelAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.P:
                e.Handled = true;
                await TogglePipAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.Escape:
                e.Handled = true;
                if (EmbyPage.Visibility == Visibility.Visible)
                {
                    HideEmbyPage();
                    break;
                }

                if (EmptyState.Visibility == Visibility.Visible && IsSettingsOpen)
                {
                    await HideSettingsPageAsync().ConfigureAwait(true);
                    break;
                }

                if (EmptyState.Visibility == Visibility.Visible && IsServerFormOpen)
                {
                    HideServerForm();
                    break;
                }

                await LeaveAnyFullscreenAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.Space:
                if (e.OriginalSource is ButtonBase)
                {
                    break;
                }

                e.Handled = true;
                await TogglePauseAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.O:
                await OpenFileAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.U:
                await OpenUrlAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.A:
                await CycleAsync("aid").ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.S:
                await CycleAsync("sid").ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.E:
                await ExportDiagnosticsAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.I:
                e.Handled = true;
                await ToggleInfoOverlayAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.N:
                await ToggleNightAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.M:
                await ToggleMuteAsync().ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.Z:
                await NudgeSubDelayAsync(-0.1).ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.X:
                await NudgeSubDelayAsync(0.1).ConfigureAwait(true);
                break;
            case (Windows.System.VirtualKey)0xDB:
                await NudgeSpeedAsync(-0.25).ConfigureAwait(true);
                break;
            case (Windows.System.VirtualKey)0xDD:
                await NudgeSpeedAsync(0.25).ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.Up:
                VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
                break;
            case Windows.System.VirtualKey.Down:
                VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
                break;
            case Windows.System.VirtualKey.PageUp:
                await PlayRelativeAsync(-1).ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.PageDown:
                await PlayRelativeAsync(1).ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.Left:
                if (_engine is not null)
                {
                    TimeSpan pos = (_engine.Snapshot.Position ?? TimeSpan.Zero) - TimeSpan.FromSeconds(5);
                    await _engine.SeekAsync(pos < TimeSpan.Zero ? TimeSpan.Zero : pos).ConfigureAwait(true);
                    ShowOsd(_ui.SeekBack);
                }

                break;
            case Windows.System.VirtualKey.Right:
                if (_engine is not null)
                {
                    await _engine.SeekAsync((_engine.Snapshot.Position ?? TimeSpan.Zero) + TimeSpan.FromSeconds(5))
                        .ConfigureAwait(true);
                    ShowOsd(_ui.SeekForward);
                }

                break;
            case Windows.System.VirtualKey.Home:
                if (_engine is not null)
                {
                    e.Handled = true;
                    await _engine.SeekAsync(TimeSpan.Zero).ConfigureAwait(true);
                }

                break;
            case Windows.System.VirtualKey.End:
                if (_engine?.Snapshot.Duration is { } end)
                {
                    e.Handled = true;
                    await _engine.SeekAsync(end).ConfigureAwait(true);
                }

                break;
            case (Windows.System.VirtualKey)0xB3:
            case (Windows.System.VirtualKey)0xFA:
                e.Handled = true;
                await RunShortcutAsync(ShortcutCommand.PlayPause).ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.Pause:
                if (e.OriginalSource is ButtonBase)
                {
                    break;
                }

                e.Handled = true;
                await RunShortcutAsync(ShortcutCommand.PlayPause).ConfigureAwait(true);
                break;
            case (Windows.System.VirtualKey)0xB2:
                e.Handled = true;
                await RunShortcutAsync(ShortcutCommand.Exit).ConfigureAwait(true);
                break;
            case (Windows.System.VirtualKey)0xB0:
                e.Handled = true;
                await RunShortcutAsync(ShortcutCommand.SeekForwardLarge).ConfigureAwait(true);
                break;
            case (Windows.System.VirtualKey)0xB1:
                e.Handled = true;
                await RunShortcutAsync(ShortcutCommand.SeekBackwardLarge).ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.GoBack:
                e.Handled = true;
                await RunShortcutAsync(ShortcutCommand.Exit).ConfigureAwait(true);
                break;
            case Windows.System.VirtualKey.GamepadA:
            case Windows.System.VirtualKey.GamepadB:
            case Windows.System.VirtualKey.GamepadX:
            case Windows.System.VirtualKey.GamepadY:
            case Windows.System.VirtualKey.GamepadDPadUp:
            case Windows.System.VirtualKey.GamepadDPadDown:
            case Windows.System.VirtualKey.GamepadDPadLeft:
            case Windows.System.VirtualKey.GamepadDPadRight:
            case Windows.System.VirtualKey.GamepadLeftShoulder:
            case Windows.System.VirtualKey.GamepadRightShoulder:
            case Windows.System.VirtualKey.GamepadMenu:
            case Windows.System.VirtualKey.GamepadView:
                if (Gamepad.Gamepads.Count > 0)
                {
                    e.Handled = true;
                    break;
                }

                e.Handled = true;
                IReadOnlyList<ShortcutCommand> mapped = PadMap.Rising(FromGamepadKey(e.Key), PadFace.None);
                if (mapped.Count > 0)
                {
                    await RunShortcutAsync(mapped[0]).ConfigureAwait(true);
                }

                break;
        }
    }

    private static bool IsKeyDown(Windows.System.VirtualKey key)
    {
        const Windows.UI.Core.CoreVirtualKeyStates down = Windows.UI.Core.CoreVirtualKeyStates.Down;
        return (InputKeyboardSource.GetKeyStateForCurrentThread(key) & down) == down;
    }

    private static bool HasModifier() =>
        IsKeyDown(Windows.System.VirtualKey.Control)
        || IsKeyDown(Windows.System.VirtualKey.Menu)
        || IsKeyDown(Windows.System.VirtualKey.LeftWindows)
        || IsKeyDown(Windows.System.VirtualKey.RightWindows);

    private static PadFace FromGamepadKey(Windows.System.VirtualKey key) =>
        key switch
        {
            Windows.System.VirtualKey.GamepadA => PadFace.A,
            Windows.System.VirtualKey.GamepadB => PadFace.B,
            Windows.System.VirtualKey.GamepadX => PadFace.X,
            Windows.System.VirtualKey.GamepadY => PadFace.Y,
            Windows.System.VirtualKey.GamepadDPadUp => PadFace.DPadUp,
            Windows.System.VirtualKey.GamepadDPadDown => PadFace.DPadDown,
            Windows.System.VirtualKey.GamepadDPadLeft => PadFace.DPadLeft,
            Windows.System.VirtualKey.GamepadDPadRight => PadFace.DPadRight,
            Windows.System.VirtualKey.GamepadLeftShoulder => PadFace.LeftShoulder,
            Windows.System.VirtualKey.GamepadRightShoulder => PadFace.RightShoulder,
            Windows.System.VirtualKey.GamepadMenu => PadFace.Menu,
            Windows.System.VirtualKey.GamepadView => PadFace.View,
            _ => PadFace.None,
        };

    /// <summary>
    /// Holds the close open until <see cref="ShutdownAsync"/> finishes, then closes
    /// for real. The second pass through here (from <see cref="Window.Close"/>) sees
    /// <c>_shutdownComplete</c> and lets the window go.
    /// </summary>
    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownComplete)
        {
            return;
        }

        args.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        try
        {
            // A hung network stop or a stuck native call must not keep the window
            // open forever; after the deadline the process is allowed to exit anyway.
            Task shutdown = ShutdownAsync();
            Task finished = await Task.WhenAny(shutdown, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(true);
            if (finished != shutdown)
            {
                Log.Warning("Shutdown exceeded 10 s; closing anyway");
            }
            else
            {
                await shutdown.ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown failed");
        }
        finally
        {
            _shutdownComplete = true;
            Close();
        }
    }

    /// <summary>
    /// Fallback for hosts without an <see cref="AppWindow"/>. Best effort only —
    /// awaits here race the window teardown, which is why the real path is
    /// <see cref="OnAppWindowClosing"/>.
    /// </summary>
    private async void OnClosed(object sender, WindowEventArgs args)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        try
        {
            await ShutdownAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown failed");
        }
        finally
        {
            _shutdownComplete = true;
        }
    }

    private async Task ShutdownAsync()
    {
        _progressTimer.Stop();
        _hideTimer.Stop();
        _osdTimer.Stop();
        _padTimer.Stop();
        Interlocked.Increment(ref _matchTicket);
        SleepPreventer.SetPlaying(false);
        await SaveProgressAsync().ConfigureAwait(true);
        await StopLibrarySessionAsync().ConfigureAwait(true);
        await RestoreDisplayRefreshAsync().ConfigureAwait(true);
        await RestoreWindowsHdrAsync().ConfigureAwait(true);
        HideThumbPreview();
        if (_thumbs is not null)
        {
            await _thumbs.DisposeAsync().ConfigureAwait(true);
            _thumbs = null;
        }

        await SaveSettingsAsync().ConfigureAwait(true);
        if (_surface is not null)
        {
            await _surface.DisposeAsync().ConfigureAwait(true);
        }

        if (_engine is not null)
        {
            await _engine.DisposeAsync().ConfigureAwait(true);
        }

        if (_store is not null)
        {
            await _store.DisposeAsync().ConfigureAwait(true);
        }

        _emby?.Dispose();
        ReleaseAcrylicBackdrop();
        await Log.CloseAndFlushAsync().ConfigureAwait(true);
    }

    private SurfaceLayout ReadLayout()
    {
        double scaleX = VideoPanel.CompositionScaleX <= 0 ? 1 : VideoPanel.CompositionScaleX;
        double scaleY = VideoPanel.CompositionScaleY <= 0 ? 1 : VideoPanel.CompositionScaleY;
        return new SurfaceLayout(VideoPanel.ActualWidth, VideoPanel.ActualHeight, scaleX, scaleY, scaleX);
    }

    private void ShowOsd(string text)
    {
        OsdText.Text = text;
        SetName(OsdText, text);
        OsdPanel.Opacity = 1;
        OsdPanel.Visibility = Visibility.Visible;
        // Sit just above the transport bar when it is showing, lower when hidden.
        OsdPanel.Margin = new Thickness(0, 0, 0, _chromeVisible && !IsPip() ? 128 : 40);
        _osdTimer.Stop();
        _osdTimer.Start();
    }

    private async Task NudgeSpeedAsync(double delta)
    {
        _speed = Math.Clamp(Math.Round(_speed + delta, 2), 0.25, 4);
        await ApplySpeedAsync().ConfigureAwait(true);
        ShowOsd(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            _ui.SpeedOsd,
            _speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private async Task NudgeSubDelayAsync(double delta)
    {
        _subDelay = Math.Round(_subDelay + delta, 2);
        await ApplySubDelayAsync().ConfigureAwait(true);
        ShowOsd(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            _ui.SubDelayOsd,
            _subDelay.ToString("+0.0;-0.0;0", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Shifts the sound against the picture for the current file only; the next load
    /// resets it (<see cref="ResetAudioDelayAsync"/>). Starts from mpv's value, so an
    /// <c>audio-delay</c> imported from mpv.conf is the base of the step.
    /// </summary>
    private async Task NudgeAudioDelayAsync(double delta)
    {
        if (_engine is null)
        {
            return;
        }

        double current = await ReadAudioDelayAsync().ConfigureAwait(true);
        await SetAudioDelayAsync(AudioDelay.Nudge(current, delta)).ConfigureAwait(true);
    }

    private async Task SetAudioDelayAsync(double seconds)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["audio-delay"] = AudioDelay.ToProperty(seconds),
        }).ConfigureAwait(true);
        ShowOsd(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            _ui.AudioDelayOsd,
            AudioDelay.FormatMilliseconds(seconds)));
    }

    private async Task<double> ReadAudioDelayAsync() =>
        _engine is null
            ? 0
            : AudioDelay.Parse(await _engine.GetPropertyStringAsync("audio-delay").ConfigureAwait(true));

    /// <summary>Audio delay is per file: every load starts in sync, whatever the previous file needed.</summary>
    private Task ResetAudioDelayAsync() =>
        ApplySettingGroupAsync("audio delay", () => _engine!.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["audio-delay"] = "0",
        }));

    private async Task ApplySpeedAsync(bool save = true)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["speed"] = _speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        }).ConfigureAwait(true);
        if (save)
        {
            await SaveSettingsAsync().ConfigureAwait(true);
        }
    }

    private async Task ApplySubDelayAsync(bool save = true)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.ApplyPropertiesAsync(new Dictionary<string, string>
        {
            ["sub-delay"] = _subDelay.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        }).ConfigureAwait(true);
        if (save)
        {
            await SaveSettingsAsync().ConfigureAwait(true);
        }
    }

    private async Task ApplySubtitleStyleAsync(bool save = true)
    {
        if (_engine is null)
        {
            return;
        }

        Dictionary<string, string> properties = new(StringComparer.Ordinal)
        {
            ["sub-ass-override"] = string.IsNullOrWhiteSpace(_settings.SubAssOverride)
                ? "no"
                : _settings.SubAssOverride,
        };
        if (!string.IsNullOrWhiteSpace(_settings.SubCodepage)
            && !_settings.SubCodepage.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            properties["sub-codepage"] = _settings.SubCodepage;
        }

        await _engine.ApplyPropertiesAsync(properties).ConfigureAwait(true);
        if (save)
        {
            await SaveSettingsAsync().ConfigureAwait(true);
        }
    }

    private async Task ApplyQualityAsync(bool save = true)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.ApplyPropertiesAsync(QualityPresetOptions.ToProperties(_settings.Quality)).ConfigureAwait(true);
        if (save)
        {
            await SaveSettingsAsync().ConfigureAwait(true);
        }
    }

    private async Task ApplyDecodingAsync(bool save = true)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.ApplyPropertiesAsync(DecodingOptions.ToProperties(_settings.HardwareDecoding)).ConfigureAwait(true);
        if (save)
        {
            await SaveSettingsAsync().ConfigureAwait(true);
        }
    }

    private async Task<string> MediaTitleAsync()
    {
        if (_engine is null)
        {
            return AppVersion.Name;
        }

        string? title = await _engine.GetPropertyStringAsync("media-title").ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        if (_currentUri is not null)
        {
            return Path.GetFileName(Uri.UnescapeDataString(_currentUri.AbsolutePath));
        }

        return AppVersion.Name;
    }

    /// <summary>Search text of the current listing; null when browsing a folder.</summary>
    private string? _librarySearch;
    private bool _librarySearchOpen;
    private Storyboard? _librarySearchAnim;
    private const double LibrarySearchExpandedWidth = 260;
    private int _libraryTicket;
    private const int LibraryWallCacheLimit = 12;
    private readonly Dictionary<string, CachedLibraryWall> _libraryWalls = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _libraryWallOrder = new();

    private async Task ShowEmbyPageAsync()
    {
        PlaylistPane.Visibility = Visibility.Collapsed;
        // The home page would otherwise stay live (and focusable) under the page.
        EmptyState.Visibility = Visibility.Collapsed;
        EmbyPage.Visibility = Visibility.Visible;
        UpdateTransportVisibility();
        _chromePinned = true;
        ShowChrome();
        await RefreshEmbyPageAsync().ConfigureAwait(true);
    }

    private void HideEmbyPage()
    {
        if (EmbyPage.Visibility != Visibility.Visible)
        {
            return;
        }

        _libraryTicket++;
        CollapseLibrarySearch(immediate: true);
        EmbyPage.Visibility = Visibility.Collapsed;
        if (_currentUri is null)
        {
            EmptyState.Visibility = Visibility.Visible;
        }

        UpdateTransportVisibility();

        _chromePinned = false;
        RestartHideTimer();
        Root.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Loads the current folder (or search) as a poster wall: the first page now,
    /// the rest through <see cref="PosterCollection"/> as the user scrolls.
    /// </summary>
    private async Task RefreshEmbyPageAsync()
    {
        int ticket = ++_libraryTicket;
        LibraryBackButton.Visibility = _libraryTrail.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryHeader.Text = LibraryBreadcrumb();
        LibraryCount.Text = "";
        if (_embyLibrary is null)
        {
            PosterGrid.ItemsSource = null;
            ShowLibraryMessage(_ui.NotConnected, offerConnect: true);
            return;
        }

        IContentProvider provider = _embyLibrary;
        LibraryMessagePanel.Visibility = Visibility.Collapsed;
        bool root = _libraryFolder is null && _librarySearch is null;
        bool sortable = _librarySearch is null && _libraryFolder is { Kind: "CollectionFolder" or "UserView" };
        SortPanel.Visibility = sortable ? Visibility.Visible : Visibility.Collapsed;
        if (!root)
        {
            ResumeSection.Visibility = Visibility.Collapsed;
            ResumeList.ItemsSource = null;
        }
        else
        {
            _ = RefreshResumeStripAsync(provider, ticket);
        }

        // Anything below a library root is an item page (hero, facts, children,
        // cast, similar titles); library roots and searches are the plain wall.
        LibraryItem? folder = _libraryFolder;
        bool detailed = _librarySearch is null && folder is { } && folder.Kind is not ("CollectionFolder" or "UserView");
        if (detailed)
        {
            PosterGrid.ItemsSource = null;
            PosterGrid.Visibility = Visibility.Collapsed;
            await ShowItemPageAsync(provider, folder!, ticket).ConfigureAwait(true);
            return;
        }

        DetailsScroll.Visibility = Visibility.Collapsed;
        UpdateHeaderBackdrop();
        if (TryShowCachedLibraryWall())
        {
            return;
        }

        PosterGrid.ItemsSource = null;
        PosterGrid.Visibility = Visibility.Collapsed;
        LibraryBusy.Visibility = Visibility.Visible;
        LibraryBusyRing.IsActive = true;

        try
        {
            string? search = _librarySearch;
            string? parent = folder?.Id;
            LibrarySort sort = _librarySort;
            LibraryPage page = search is not null
                ? new LibraryPage(await provider.SearchAsync(search).ConfigureAwait(true), null)
                : await provider.BrowsePageAsync(parent, 0, folder, sort).ConfigureAwait(true);
            if (ticket != _libraryTicket)
            {
                return;
            }

            if (page.Items.Count == 0)
            {
                ShowLibraryMessage(search is not null ? _ui.LibraryNoResults : _ui.LibraryEmpty);
                return;
            }

            // One geometry per listing: episodes / libraries are 16:9, everything else 2:3.
            _posterLandscape = page.Items.All(i => i.HasLandscapeArt);
            LayoutPosterGrid();
            List<PosterRow> rows = page.Items.Select(i => PosterFor(provider, i)).ToList();
            int? total = page.TotalCount;
            bool more = search is null && total is { } t && t > rows.Count;
            PosterCollection collection = new(
                rows,
                more,
                async (start, cancellationToken) =>
                {
                    LibraryPage next = await provider.BrowsePageAsync(parent, start, folder, sort, cancellationToken).ConfigureAwait(true);
                    IReadOnlyList<PosterRow> nextRows = next.Items.Select(i => PosterFor(provider, i)).ToList();
                    int loaded = start + nextRows.Count;
                    return (nextRows, next.TotalCount is { } count ? loaded < count : nextRows.Count >= provider.PageSize);
                },
                ex =>
                {
                    Log.Warning(ex, "Library page load failed");
                    ShowError(DescribeEmbyFailure(ex));
                });
            LibraryCount.Text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _ui.LibraryItemCount,
                (total ?? rows.Count).ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
            PosterGrid.ItemsSource = collection;
            PosterGrid.Visibility = Visibility.Visible;
            RememberLibraryWall(collection, total ?? rows.Count);
            // The panel only exists once items are realised; size its cells now.
            PosterGrid.UpdateLayout();
            LayoutPosterGrid();
        }
        catch (EmbyHttpException ex) when (ex.Kind == EmbyFailureKind.Unauthorized)
        {
            // The remembered token was revoked or expired: say so and offer sign-in.
            Log.Warning(ex, "Library browse rejected the saved token");
            ShowLibraryMessage(_ui.EmbySessionExpired, offerConnect: true);
        }
        catch (Exception ex)
        {
            if (ticket != _libraryTicket)
            {
                return; // superseded by a newer navigation; nothing to show
            }

            Log.Error(ex, "Library browse failed");
            ShowLibraryMessage(DescribeEmbyFailure(ex), offerConnect: ex is EmbyHttpException);
        }
        finally
        {
            if (ticket == _libraryTicket)
            {
                LibraryBusyRing.IsActive = false;
                LibraryBusy.Visibility = Visibility.Collapsed;
            }
        }
    }

    private sealed class CachedLibraryWall
    {
        public required PosterCollection Collection { get; init; }
        public required bool Landscape { get; init; }
        public required int Total { get; init; }
    }

    private string LibraryWallCacheKey()
    {
        string server = _settings.ActiveServerId ?? "";
        if (_librarySearch is not null)
        {
            return server + "|s|" + _librarySearch;
        }

        return server + "|f|" + (_libraryFolder?.Id ?? "") + "|" + _librarySort.Field + "|" + (_librarySort.Descending ? "d" : "a");
    }

    private bool TryShowCachedLibraryWall()
    {
        string key = LibraryWallCacheKey();
        if (!_libraryWalls.TryGetValue(key, out CachedLibraryWall? cached))
        {
            return false;
        }

        if (_libraryWallOrder.First is { } head && head.Value != key
            && _libraryWallOrder.Find(key) is { } node)
        {
            _libraryWallOrder.Remove(node);
            _libraryWallOrder.AddFirst(key);
        }

        _posterLandscape = cached.Landscape;
        LibraryCount.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            _ui.LibraryItemCount,
            cached.Total.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
        PosterGrid.ItemsSource = cached.Collection;
        PosterGrid.Visibility = Visibility.Visible;
        PosterGrid.UpdateLayout();
        LayoutPosterGrid();
        return true;
    }

    private void RememberLibraryWall(PosterCollection collection, int total)
    {
        string key = LibraryWallCacheKey();
        _libraryWalls[key] = new CachedLibraryWall
        {
            Collection = collection,
            Landscape = _posterLandscape,
            Total = total,
        };
        if (_libraryWallOrder.Find(key) is { } existing)
        {
            _libraryWallOrder.Remove(existing);
        }

        _libraryWallOrder.AddFirst(key);
        while (_libraryWallOrder.Count > LibraryWallCacheLimit && _libraryWallOrder.Last is { } last)
        {
            _libraryWallOrder.RemoveLast();
            _libraryWalls.Remove(last.Value);
        }
    }

    /// <summary>"服务器名 / 当前页面": the server as saved on the home page, then where we are.</summary>
    private string LibraryBreadcrumb()
    {
        string server = _settings.ActiveServer?.DisplayName ?? "Emby";
        string page = _librarySearch is not null
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.LibrarySearchTitle, _librarySearch)
            : _libraryFolder?.Name ?? _ui.KindLibrary;
        return server + "  /  " + page;
    }

    /// <summary>
    /// The player chrome (transport, 播放信息, back) belongs to the playback
    /// window: gone on the home page and while the library page is up.
    /// </summary>
    private bool IsPlaybackWindow()
    {
        bool loaded = _engine is not null && _engine.Snapshot.MediaPhase is not (MediaPhase.Empty or MediaPhase.Failed);
        bool pageUp = EmbyPage.Visibility == Visibility.Visible || EmptyState.Visibility == Visibility.Visible;
        return loaded && !pageUp;
    }

    private void UpdateTransportVisibility()
    {
        Visibility wanted = IsPlaybackWindow() ? Visibility.Visible : Visibility.Collapsed;
        if (TransportBar.Visibility != wanted)
        {
            TransportBar.Visibility = wanted;
        }

        if (InfoButton.Visibility != wanted)
        {
            InfoButton.Visibility = wanted;
        }

        if (wanted != Visibility.Visible && InfoOverlay.Visibility == Visibility.Visible)
        {
            InfoOverlay.Visibility = Visibility.Collapsed;
        }

        BackToLibraryButton.Visibility = wanted;
        string back = _currentLibraryItem is null ? _ui.BackToHome : _ui.BackToDetails;
        ToolTipService.SetToolTip(BackToLibraryButton, back);
        SetName(BackToLibraryButton, back);

        // Settings sits at the bottom-left of the home column; the title-bar
        // copy is only for playback / the library page.
        SettingsButton.Visibility = EmptyState.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        UpdatePageBackdrop();
    }

    /// <summary>
    /// Home and the library sit on a desktop acrylic backdrop. The swap chain
    /// is opaque, so it has to be put away or the frost cannot show through.
    /// Playback covers the window again with a solid bed.
    /// </summary>
    private void UpdatePageBackdrop()
    {
        bool pages = EmptyState.Visibility == Visibility.Visible
            || EmbyPage.Visibility == Visibility.Visible;
        Visibility video = pages ? Visibility.Collapsed : Visibility.Visible;
        if (VideoPanel.Visibility != video)
        {
            VideoPanel.Visibility = video;
            if (video == Visibility.Visible)
            {
                VideoPanel.UpdateLayout();
            }
        }
        if (_highContrast)
        {
            ReleaseAcrylicBackdrop();
            return;
        }

        bool glass = pages && EnsureAcrylicBackdrop();
        Brush solid = Brush("AppBackgroundBrush");
        Brush page = glass ? Brush("PageGlassBrush") : solid;
        Root.Background = glass ? Brush("TransparentBrush") : solid;
        EmptyState.Background = page;
        EmbyPage.Background = page;
        HomeLeft.Background = glass ? Brush("HomeColumnGlassBrush") : Brush("HomeColumnBrush");
        SettingsPanel.Background = glass ? Brush("TransparentBrush") : solid;
    }

    /// <summary>
    /// Host-backdrop acrylic (blur of whatever is behind the window). Falls back
    /// to an opaque page when the OS does not offer the material.
    /// </summary>
    private bool EnsureAcrylicBackdrop()
    {
        if (_acrylic is not null)
        {
            return true;
        }

        if (!DesktopAcrylicController.IsSupported())
        {
            return false;
        }

        try
        {
            SystemBackdrop = null;
            _acrylicConfig = new SystemBackdropConfiguration
            {
                IsInputActive = _windowFocused,
                Theme = SystemBackdropTheme.Dark,
            };
            _acrylic = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Thin,
            };
            _acrylic.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            _acrylic.SetSystemBackdropConfiguration(_acrylicConfig);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Desktop acrylic backdrop is not available");
            ReleaseAcrylicBackdrop();
            return false;
        }
    }

    private void ReleaseAcrylicBackdrop()
    {
        if (_acrylic is not null)
        {
            try
            {
                _acrylic.RemoveAllSystemBackdropTargets();
            }
            catch (Exception)
            {
            }

            _acrylic.Dispose();
            _acrylic = null;
        }

        _acrylicConfig = null;
        SystemBackdrop = null;
    }

    /// <summary>Geometry of the current wall: base tile sizes, fitted to the window by <see cref="LayoutPosterGrid"/>.</summary>
    private bool _posterLandscape;
    private double _posterTileWidth = 150;
    private double _posterImageHeight = 225;
    private LibrarySort _librarySort = LibrarySort.Default;

    private const double PosterBaseWidth = 150;
    private const double ThumbBaseWidth = 232;
    private const double PosterMinGap = 14;

    /// <summary>
    /// Fits whole columns into the wall's width: cells share the width evenly (so
    /// the outer margins stay equal to the page padding and the gaps between
    /// tiles absorb the remainder) and tiles grow up to 25 % to use the room.
    /// </summary>
    private void LayoutPosterGrid()
    {
        // Collapsed grids keep a stale ActualWidth; the page itself is always laid out.
        double width = PosterGrid.Visibility == Visibility.Visible && PosterGrid.ActualWidth > 0
            ? PosterGrid.ActualWidth
            : EmbyPage.ActualWidth - PosterGrid.Margin.Left - PosterGrid.Margin.Right;
        (_posterTileWidth, _posterImageHeight) = FitWall(PosterGrid, width, _posterLandscape);
    }

    /// <summary>
    /// Fits whole columns of <paramref name="landscape"/> (16:9) or portrait (2:3)
    /// tiles into <paramref name="width"/>: cells share the width evenly, tiles grow
    /// up to 25 %, and every row already in the grid is resized to match.
    /// </summary>
    private static (double TileWidth, double ImageHeight) FitWall(GridView grid, double width, bool landscape)
    {
        double available = width - grid.Padding.Left - grid.Padding.Right;
        double baseWidth = landscape ? ThumbBaseWidth : PosterBaseWidth;
        double ratio = landscape ? 9.0 / 16.0 : 1.5;
        double tile;
        double cell;
        if (available < baseWidth)
        {
            tile = baseWidth;
            cell = baseWidth + PosterMinGap;
        }
        else
        {
            int columns = Math.Max(1, (int)Math.Floor((available + PosterMinGap) / (baseWidth + PosterMinGap)));
            cell = Math.Floor(available / columns);
            tile = Math.Floor(Math.Min(baseWidth * 1.25, cell - PosterMinGap));
        }

        double height = Math.Round(tile * ratio);
        if (grid.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            panel.ItemWidth = cell;
        }

        if (grid.ItemsSource is IEnumerable<PosterRow> rows)
        {
            foreach (PosterRow row in rows)
            {
                row.Resize(tile, height);
            }
        }

        return (tile, height);
    }

    private PosterRow PosterFor(IContentProvider provider, LibraryItem item, bool withSeries = false, bool strip = false) =>
        PosterFor(provider, item, strip ? ThumbBaseWidth : _posterTileWidth, strip ? Math.Round(ThumbBaseWidth * 9 / 16) : _posterImageHeight, withSeries);

    /// <param name="width">Tile width: the wall's fitted size, or a strip's fixed one.</param>
    private PosterRow PosterFor(IContentProvider provider, LibraryItem item, double width, double height, bool withSeries)
    {
        Uri? image = provider.ImageUrl(item, (int)(width * 2), (int)(height * 2));
        string resume = "";
        if (item.PlayedPercentage is > 0 and < 100)
        {
            resume = item.Remaining is { } left
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.ContinueWatchingRemaining, Math.Max(1, (int)Math.Round(left.TotalMinutes)))
                : _ui.ContinueWatching;
        }

        return new PosterRow(item, item.Name, PosterSubtitle(item, withSeries || _librarySearch is not null), image, width, height, resume);
    }

    // ---- sorting -------------------------------------------------------------

    private void BuildSortMenu()
    {
        SortFlyout.Items.Clear();
        foreach (string field in LibrarySort.Fields)
        {
            RadioMenuFlyoutItem item = new() { Text = SortFieldName(field), Tag = field, GroupName = "sort", IsChecked = field == _librarySort.Field };
            item.Click += async (_, _) =>
            {
                _librarySort = new LibrarySort(field, LibrarySort.DefaultsToDescending(field));
                UpdateSortUi();
                await RefreshEmbyPageAsync().ConfigureAwait(true);
            };
            SortFlyout.Items.Add(item);
        }

        UpdateSortUi();
    }

    private void UpdateSortUi()
    {
        SortText.Text = SortFieldName(_librarySort.Field);
        SortOrderGlyph.Glyph = _librarySort.Descending ? "\uE74B" : "\uE74A";
        string order = _librarySort.Descending ? _ui.SortDescending : _ui.SortAscending;
        ToolTipService.SetToolTip(SortOrderButton, order);
        SetName(SortOrderButton, order);
        foreach (RadioMenuFlyoutItem item in SortFlyout.Items.OfType<RadioMenuFlyoutItem>())
        {
            item.IsChecked = (string?)item.Tag == _librarySort.Field;
        }
    }

    private string SortFieldName(string field) => field switch
    {
        "SortName" => _ui.SortSortName,
        "DateCreated" => _ui.SortDateCreated,
        "PremiereDate" => _ui.SortPremiereDate,
        "ProductionYear" => _ui.SortProductionYear,
        "CommunityRating" => _ui.SortCommunityRating,
        "Runtime" => _ui.SortRuntime,
        "PlayCount" => _ui.SortPlayCount,
        "DatePlayed" => _ui.SortDatePlayed,
        "Random" => _ui.SortRandom,
        _ => field,
    };

    /// <summary>Back on the player: a server title returns to its item page, a local file to home.</summary>
    private Task ReturnFromPlaybackAsync() =>
        _currentLibraryItem is not null ? ReturnToLibraryAsync() : ReturnToHomeAsync();

    /// <summary>Stop a local file (or URL) and land on the home page.</summary>
    private async Task ReturnToHomeAsync()
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            await SaveProgressAsync().ConfigureAwait(true);
            await StopLibrarySessionAsync().ConfigureAwait(true);
            await _engine.StopPlaybackAsync().ConfigureAwait(true);
            await RestoreDisplayRefreshAsync().ConfigureAwait(true);
            await RestoreWindowsHdrAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Stop before returning home failed");
        }

        _currentUri = null;
        _currentSourceKind = MediaSourceKind.Unknown;
        _progressKey = null;
        _currentLibraryItem = null;
        _prefetchedNext = null;
        TitleBadges.Children.Clear();
        SetMediaTitle(AppVersion.Name);
        PlaylistPane.Visibility = Visibility.Collapsed;
        HideEmbyPage();
        EmptyState.Visibility = Visibility.Visible;
        await RefreshChannelsAsync().ConfigureAwait(true);
        UpdateTransportVisibility();
        Root.Focus(FocusState.Programmatic);
    }

    /// <summary>Stop button on the player: close the session and go back to the item's page.</summary>
    private async Task ReturnToLibraryAsync()
    {
        if (_engine is null)
        {
            return;
        }

        LibraryItem? item = _currentLibraryItem;
        try
        {
            await SaveProgressAsync().ConfigureAwait(true);
            await StopLibrarySessionAsync().ConfigureAwait(true);
            await _engine.StopPlaybackAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Stop before returning to the library failed");
        }

        _currentUri = null;
        _currentSourceKind = MediaSourceKind.Unknown;
        _progressKey = null;
        _currentLibraryItem = null;
        _prefetchedNext = null;
        TitleBadges.Children.Clear();
        SetMediaTitle(AppVersion.Name);
        await RefreshChannelsAsync().ConfigureAwait(true);
        if (item is not null && _libraryFolder?.Id != item.Id)
        {
            // Auto-continue moved on since the page was left: land on what was playing.
            _libraryTrail.Add(_libraryFolder);
            _libraryFolder = item;
        }

        await ShowEmbyPageAsync().ConfigureAwait(true);
    }

    /// <summary>The "continue watching" strip: only on the top level, hidden when there is nothing in progress.</summary>
    private async Task RefreshResumeStripAsync(IContentProvider provider, int ticket)
    {
        try
        {
            IReadOnlyList<LibraryItem> items = await provider.ContinueWatchingAsync().ConfigureAwait(true);
            if (ticket != _libraryTicket)
            {
                return;
            }

            if (items.Count == 0)
            {
                ResumeSection.Visibility = Visibility.Collapsed;
                return;
            }

            List<PosterRow> rows = items.Select(i => PosterFor(provider, i, withSeries: true, strip: true)).ToList();
            Log.Information(
                "Library: continue watching {Count} item(s): {Items}",
                rows.Count,
                string.Join("; ", rows.Select(r => $"{r.Item.EpisodeCode ?? r.Item.Name} {r.Item.PlayedPercentage?.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) ?? "-"}% label='{r.ResumeLabel}'")));
            ResumeList.ItemsSource = rows;
            ResumeSection.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Continue-watching list failed");
            if (ticket == _libraryTicket)
            {
                ResumeSection.Visibility = Visibility.Collapsed;
            }
        }
    }

    private string PosterSubtitle(LibraryItem item, bool withSeries)
    {
        string kind = item.Kind switch
        {
            "Movie" => _ui.KindMovie,
            "Series" => _ui.KindSeries,
            "Season" => _ui.KindSeason,
            "Episode" => _ui.KindEpisode,
            "BoxSet" => _ui.KindBoxSet,
            "Playlist" => _ui.KindPlaylist,
            "Folder" => _ui.KindFolder,
            "CollectionFolder" or "UserView" => item.CollectionType switch
            {
                "movies" => _ui.KindMovie,
                "tvshows" => _ui.KindSeries,
                "music" => _ui.KindMusic,
                "boxsets" => _ui.KindBoxSet,
                "playlists" => _ui.KindPlaylist,
                _ => _ui.KindLibrary,
            },
            _ => item.Kind,
        };
        return item.Kind switch
        {
            "Episode" => string.Join("  ·  ", new[] { withSeries ? item.SeriesName : null, item.EpisodeCode }.Where(s => !string.IsNullOrEmpty(s))),
            "Season" => item.SeriesName ?? kind,
            "Movie" or "Series" => item.ProductionYear is { } year ? year.ToString(System.Globalization.CultureInfo.InvariantCulture) + "  ·  " + kind : kind,
            _ => kind,
        };
    }

    private void ShowLibraryMessage(string message, bool offerConnect = false)
    {
        LibraryBusyRing.IsActive = false;
        LibraryBusy.Visibility = Visibility.Collapsed;
        LibraryMessage.Text = message;
        LibraryConnectButton.Visibility = offerConnect ? Visibility.Visible : Visibility.Collapsed;
        LibraryMessagePanel.Visibility = Visibility.Visible;
        PosterGrid.Visibility = Visibility.Collapsed;
    }

    private async Task SearchLibraryAsync(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return;
        }

        if (_librarySearch is null)
        {
            // Back from the results returns to the folder the search started in.
            _libraryTrail.Add(_libraryFolder);
        }

        _librarySearch = query;
        await RefreshEmbyPageAsync().ConfigureAwait(true);
    }

    private void OnLibrarySearchButtonClick(object sender, RoutedEventArgs e) => ToggleLibrarySearch();

    private void ToggleLibrarySearch()
    {
        if (_librarySearchOpen)
        {
            CollapseLibrarySearch();
            return;
        }

        ExpandLibrarySearch();
    }

    private void ExpandLibrarySearch()
    {
        _librarySearchOpen = true;
        LibrarySearchBox.IsHitTestVisible = true;
        LibrarySearchBox.IsTabStop = true;
        if (_reduceMotion)
        {
            ApplyLibrarySearchOpen(LibrarySearchExpandedWidth, 1);
        }
        else
        {
            AnimateLibrarySearch(LibrarySearchExpandedWidth, 1);
        }

        LibrarySearchBox.Focus(FocusState.Programmatic);
    }

    private void CollapseLibrarySearch(bool immediate = false)
    {
        _librarySearchOpen = false;
        LibrarySearchBox.IsHitTestVisible = false;
        LibrarySearchBox.IsTabStop = false;
        if (_librarySearch is null)
        {
            LibrarySearchBox.Text = "";
        }

        if (immediate || _reduceMotion)
        {
            ApplyLibrarySearchOpen(0, 0);
            return;
        }

        AnimateLibrarySearch(0, 0);
    }

    private void ApplyLibrarySearchOpen(double width, double opacity)
    {
        _librarySearchAnim?.Stop();
        _librarySearchAnim = null;
        LibrarySearchClip.Width = width;
        LibrarySearchBox.Opacity = opacity;
        ClipLibrarySearch();
    }

    private void AnimateLibrarySearch(double width, double opacity)
    {
        // Hold the current width before Stop(), otherwise the storyboard snaps back.
        double from = LibrarySearchClip.ActualWidth;
        _librarySearchAnim?.Stop();
        LibrarySearchClip.Width = from;
        Duration duration = new(TimeSpan.FromMilliseconds(260));
        CubicEase ease = new() { EasingMode = EasingMode.EaseOut };
        DoubleAnimation widthAnim = LibrarySearchAnimation(LibrarySearchClip, "Width", width, duration, ease);
        widthAnim.From = from;
        widthAnim.EnableDependentAnimation = true;
        Storyboard storyboard = new();
        storyboard.Children.Add(widthAnim);
        storyboard.Children.Add(LibrarySearchAnimation(LibrarySearchBox, "Opacity", opacity, duration, ease));
        storyboard.Completed += (_, _) =>
        {
            LibrarySearchClip.Width = width;
            LibrarySearchBox.Opacity = opacity;
            ClipLibrarySearch();
        };
        _librarySearchAnim = storyboard;
        storyboard.Begin();
    }

    private static DoubleAnimation LibrarySearchAnimation(
        DependencyObject target,
        string property,
        double to,
        Duration duration,
        EasingFunctionBase ease)
    {
        DoubleAnimation animation = new()
        {
            To = to,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    private void ClipLibrarySearch()
    {
        double width = Math.Max(0, LibrarySearchClip.ActualWidth);
        double height = Math.Max(0, LibrarySearchClip.ActualHeight);
        LibrarySearchClip.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, width, height),
        };
    }

    private string DescribeEmbyFailure(Exception ex) => ex switch
    {
        EmbyHttpException { Kind: EmbyFailureKind.CloudflareBlocked } => _ui.ConnectBlockedByFirewall,
        EmbyHttpException { Kind: EmbyFailureKind.Unauthorized } => _ui.ConnectBadCredentials,
        EmbyHttpException { Kind: EmbyFailureKind.HtmlPage } => _ui.ConnectNotEmby,
        // mpv could not fetch the stream: the server has the entry but not the file.
        InvalidOperationException when ex.Message.Contains("HTTP error 404", StringComparison.Ordinal) => _ui.ServerFileMissing,
        TaskCanceledException or OperationCanceledException => "timeout",
        HttpRequestException { InnerException: { } inner } => inner.Message,
        _ => ex.Message,
    };

    /// <summary>
    /// Reachability check without touching the saved account: public server info,
    /// then (when both fields are filled) a sign-in that is thrown away afterwards.
    /// </summary>
    private async Task<string> TestEmbyConnectionAsync(string urlText, string userName, string password)
    {
        if (!Uri.TryCreate(urlText.Trim(), UriKind.Absolute, out Uri? url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.ConnectFailed, "URL");
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(12));
        string result;
        try
        {
            System.Text.Json.JsonElement info = await EmbyClient.GetPublicInfoAsync(url, timeout.Token).ConfigureAwait(true);
            string serverName = info.TryGetProperty("ServerName", out System.Text.Json.JsonElement n) ? n.ToString() : url.Host;
            string version = info.TryGetProperty("Version", out System.Text.Json.JsonElement v) ? v.ToString() : "?";
            result = string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.EmbyTestOk, serverName, version);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Emby test connection failed");
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.EmbyTestFailed, DescribeEmbyFailure(ex));
        }

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return result;
        }

        // Server reachable: the second line reports the sign-in separately.
        try
        {
            using EmbyClient probe = new(url, Environment.MachineName, "penrose-" + Environment.MachineName, AppVersion.Display);
            EmbySession session = await probe.AuthenticateAsync(userName.Trim(), password, timeout.Token).ConfigureAwait(true);
            return result + "\n" + string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.EmbyTestLoginOk, session.UserName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Emby test sign-in failed");
            return result + "\n" + string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.ConnectFailed, DescribeEmbyFailure(ex));
        }
    }

    private async Task OnPosterClickAsync(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PosterRow row)
        {
            return;
        }

        // Folders drill in; playable items open their page (play lives there).
        // Opening something from search results leaves the search behind it.
        _libraryQueue = ((sender as ItemsControl)?.ItemsSource as IEnumerable<PosterRow> ?? [])
            .Select(r => r.Item)
            .Where(i => !i.IsFolder)
            .ToList();
        _libraryTrail.Add(_librarySearch is null ? _libraryFolder : null);
        _libraryFolder = row.Item;
        _librarySearch = null;
        LibrarySearchBox.Text = "";
        CollapseLibrarySearch(immediate: true);
        await RefreshEmbyPageAsync().ConfigureAwait(true);
    }

    private async Task OnResumeClickAsync(ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PosterRow row)
        {
            return;
        }

        List<LibraryItem> queue = (ResumeList.ItemsSource as IEnumerable<PosterRow> ?? []).Select(r => r.Item).ToList();
        await PlayLibraryItemAsync(row.Item, null, queue).ConfigureAwait(true);
    }

    private async Task LibraryBackAsync()
    {
        if (_libraryTrail.Count == 0)
        {
            return;
        }

        _libraryFolder = _libraryTrail[^1];
        _libraryTrail.RemoveAt(_libraryTrail.Count - 1);
        _librarySearch = null;
        LibrarySearchBox.Text = "";
        CollapseLibrarySearch(immediate: true);
        await RefreshEmbyPageAsync().ConfigureAwait(true);
    }

    private void ResetLibraryNavigation()
    {
        _libraryTicket++;
        _libraryFolder = null;
        _librarySearch = null;
        _libraryTrail.Clear();
        LibrarySearchBox.Text = "";
        CollapseLibrarySearch(immediate: true);
        PosterGrid.ItemsSource = null;
        DetailsScroll.Visibility = Visibility.Collapsed;
        _libraryWalls.Clear();
        _libraryWallOrder.Clear();
        PosterImageCache.Clear();
    }

    /// <param name="resolved">A PlaybackInfo result obtained earlier (prefetched next episode).</param>
    /// <param name="queue">The listing the item was chosen from, for Next / Previous.</param>
    /// <param name="fromStart">Ignore every resume point ("从头播放").</param>
    /// <param name="selection">Version / audio / subtitle picked on the item page; null for the defaults.</param>
    private async Task PlayLibraryItemAsync(
        LibraryItem item,
        PlaybackCandidate? resolved = null,
        IReadOnlyList<LibraryItem>? queue = null,
        bool fromStart = false,
        PlaybackSelection? selection = null)
    {
        if (_engine is null || _store is null || _embyResolver is null)
        {
            ShowError(_ui.NotConnected);
            return;
        }

        _suppressPlaylistAdvance = true;
        _prefetchStarted = false;
        _prefetchedNext = null;
        // Remote resolve + open can take a while; the page shows it is working.
        bool busyOnPage = EmbyPage.Visibility == Visibility.Visible;
        if (busyOnPage)
        {
            LibraryBusy.Visibility = Visibility.Visible;
            LibraryBusyRing.IsActive = true;
            PosterGrid.IsHitTestVisible = false;
        }

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await SaveProgressAsync().ConfigureAwait(true);
            await StopLibrarySessionAsync().ConfigureAwait(true);
            await RestoreDisplayRefreshAsync().ConfigureAwait(true);
            await RestoreWindowsHdrAsync().ConfigureAwait(true);
            // Built from this session, not from a fixture: audio policy, pinned
            // device, current display and the hwdec method mpv actually uses.
            string? hwdec = await _engine.GetPropertyStringAsync("hwdec-current").ConfigureAwait(true);
            PlaybackCapabilitySnapshot caps = DeviceProfileBuilder.FromRuntime(
                _audioPolicy,
                _settings.AudioPassthrough,
                _settings.AudioDevice,
                DisplayIsAdvancedColor(),
                hwdec,
                _surface?.CurrentDisplay?.AdapterLuid);
            long prepared = clock.ElapsedMilliseconds;
            PlaybackCandidate candidate = resolved ?? await _embyResolver.ResolveAsync(item.Id, caps, selection?.MediaSourceId).ConfigureAwait(true);
            long resolvedAt = clock.ElapsedMilliseconds;
            PlaybackRequest request = candidate.ToPlaybackRequest(Guid.NewGuid());
            if (selection is not null && candidate.Method != PlayMethod.Transcode)
            {
                // Direct play keeps the container's track order, so the page's picks
                // map onto mpv track numbers; a transcode has whatever the server muxed.
                (int? aid, int? sid) = TrackNumbersFor(selection, candidate.MediaSourceId);
                request = request with { AudioTrack = aid, SubtitleTrack = sid };
            }

            string progressKey = candidate.ProgressKey;
            PlaybackProgressRecord? saved = _settings.RememberPlaybackPosition && !fromStart
                ? await _store.GetProgressAsync(progressKey).ConfigureAwait(true)
                : null;
            if (saved is not null && PlaybackResume.ShouldRestore(saved.PositionMs, saved.DurationMs))
            {
                request = request with { StartPosition = TimeSpan.FromMilliseconds(saved.PositionMs) };
            }
            else if (_settings.RememberPlaybackPosition
                && !fromStart
                && item.ResumePosition is { } serverPosition
                && PlaybackResume.ShouldRestore((long)serverPosition.TotalMilliseconds, (long?)item.RunTime?.TotalMilliseconds))
            {
                // Watched partway on another device: the server's resume point.
                request = request with { StartPosition = serverPosition };
            }

            _currentUri = request.Uri;
            _currentSourceKind = request.SourceKind;
            _progressKey = progressKey;
            _currentLibraryItem = item;
            _libraryQueue = queue ?? _libraryQueue;
            HideError();
            await ResetAudioDelayAsync().ConfigureAwait(true);
            // No warm-up here: measured on a cloud-backed Emby, a parallel range
            // request only queues behind mpv's own and makes the open slower. The
            // next episode is warmed ahead of time instead (PrefetchNextEpisodeAsync).
            await _engine.LoadAsync(request).ConfigureAwait(true);
            Log.Information(
                "Play: library {Name} [{Id}] method={Method} host={Host} source={MediaSourceId} prepare={PrepareMs}ms resolve={ResolveMs}ms open={OpenMs}ms start={Start} prefetched={Prefetched}",
                item.Name,
                item.Id,
                candidate.Method,
                request.Uri.IsFile ? "file" : request.Uri.Authority,
                candidate.MediaSourceId,
                prepared,
                resolvedAt - prepared,
                clock.ElapsedMilliseconds - resolvedAt,
                request.StartPosition,
                resolved is not null);
            if (request.AudioTrack is not null || request.SubtitleTrack is not null)
            {
                Log.Information("Play: tracks picked on the page aid={Aid} sid={Sid}", request.AudioTrack, request.SubtitleTrack);
            }

            // The video is running: uncover it now, before the (slow, remote) start
            // report — otherwise the page hides the first seconds of playback.
            EmptyState.Visibility = Visibility.Collapsed;
            HideEmbyPage();
            UpdateTransportVisibility();
            SetMediaTitle(item.Name);
            if (_surface is { IsTopLevel: false })
            {
                await _surface.AttachAsync().ConfigureAwait(true);
            }

            if (request.ReportingContext is { PlaySessionId: not null } reporting && _emby is not null)
            {
                _reporting = reporting;
                _session ??= new SessionReportCoordinator(new EmbyPlaybackReporter(_emby, _settings.ActiveServer?.UserId));
                _session.Bind(reporting.PlaySessionId, _engine.Snapshot.PlaybackGeneration, reuseSession: false);
                try
                {
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(8));
                    await _session.StartAsync(
                            reporting,
                            request.StartPosition ?? TimeSpan.Zero,
                            _engine.Snapshot.PlaybackGeneration,
                            timeout.Token)
                        .ConfigureAwait(true);
                    Log.Information("Emby: session started play={PlaySessionId} method={PlayMethod}", reporting.PlaySessionId, reporting.PlayMethod);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
                {
                    // Video is already playing; a failed Start report is not a playback failure.
                    Log.Warning(ex, "Emby session start report failed");
                }
            }

#if DEBUG
            // PENROSE_DEBUG_SEEK_TO_END_MINUS=<seconds>: land near the end so the
            // prefetch / auto-continue path can be exercised without watching it all.
            if (Environment.GetEnvironmentVariable("PENROSE_DEBUG_SEEK_TO_END_MINUS") is { Length: > 0 } tail
                && double.TryParse(tail, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds)
                && _engine.Snapshot.Duration is { } total
                && total.TotalSeconds > seconds)
            {
                await _engine.SeekAsync(total - TimeSpan.FromSeconds(seconds)).ConfigureAwait(true);
            }
#endif

            _ = ShowFormatBadgesAsync();
            await RefreshChaptersAsync().ConfigureAwait(true);
            await MatchDisplayRefreshAsync().ConfigureAwait(true);
            await RefreshStatusAsync().ConfigureAwait(true);
            ShowChrome();
            RestartHideTimer();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Library play failed");
            ShowError(DescribeEmbyFailure(ex));
        }
        finally
        {
            _suppressPlaylistAdvance = false;
            if (busyOnPage)
            {
                PosterGrid.IsHitTestVisible = true;
                LibraryBusyRing.IsActive = false;
                LibraryBusy.Visibility = Visibility.Collapsed;
            }
        }
    }

    // (server connection management lives in MainWindow.Home.cs)

    private async Task StopLibrarySessionAsync()
    {
        if (_session is null || _reporting is null || _engine is null)
        {
            _reporting = null;
            return;
        }

        ReportingContext context = _reporting;
        _reporting = null;
        try
        {
            // Bounded so a dead server cannot stall file switching or window close.
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            TimeSpan position = _engine.Snapshot.Position ?? TimeSpan.Zero;
            await _session.StopAsync(
                    context,
                    position,
                    _engine.Snapshot.PlaybackGeneration,
                    timeout.Token)
                .ConfigureAwait(true);
            Log.Information("Emby: session stopped play={PlaySessionId} position={Position}", context.PlaySessionId, position);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Emby session stop failed");
        }
    }

    /// <summary>A settings card: section header plus rows, on the shared pane style.</summary>
    private static Border SettingsSection(string title, params UIElement[] rows)
    {
        StackPanel body = new() { Spacing = 2 };
        body.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["SectionHeaderText"],
            Margin = new Thickness(8, 4, 0, 6),
        });
        foreach (UIElement row in rows)
        {
            body.Children.Add(row);
        }

        return new Border
        {
            Style = (Style)Application.Current.Resources["Pane"],
            Padding = new Thickness(6),
            Child = body,
        };
    }

    /// <summary>Label (+ optional description) on the left, the control on the right.</summary>
    private static Grid SettingsRow(string label, FrameworkElement control, string? description = null)
    {
        Grid row = new() { Padding = new Thickness(8, 8, 8, 8), ColumnSpacing = 16, MinHeight = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["BodyText"],
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Children.Add(new TextBlock
            {
                Text = description,
                Style = (Style)Application.Current.Resources["SecondaryText"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        row.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    /// <summary>A full-width row (text boxes, status lines, button groups).</summary>
    private static Grid SettingsBlock(FrameworkElement content)
    {
        Grid row = new() { Padding = new Thickness(8, 6, 8, 6) };
        row.Children.Add(content);
        return row;
    }

    /// <summary>
    /// Header-less switch. The default template reserves ~154 dip for header and
    /// on/off text; with empty content and MinWidth 0 it shrinks to the knob.
    /// </summary>
    private static ToggleSwitch Toggle(bool on) =>
        new()
        {
            IsOn = on,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            Width = 52,
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

    private void PopulateSettingsPage() =>
        _settingsPage = BuildSettingsContent(SettingsHost);

    private async Task ShowSettingsDialogAsync()
    {
        if (_settingsDialogOpen)
        {
            return;
        }

        _settingsDialogOpen = true;
        try
        {
            StackPanel panel = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            ContentDialog? dialog = null;
            bool launchWizard = false;
            SettingsPageControls page = BuildSettingsContent(panel, () =>
            {
                launchWizard = true;
                dialog?.Hide();
            });
            ScrollViewer scroller = new()
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                MaxHeight = Math.Clamp(Root.ActualHeight - 240, 280, 760),
                Width = Math.Clamp(Root.ActualWidth - 160, 520, 680),
                Padding = new Thickness(0, 0, 8, 0),
            };
            dialog = new ContentDialog
            {
                Title = _ui.Settings,
                PrimaryButtonText = _ui.Save,
                CloseButtonText = _ui.Cancel,
                DefaultButton = ContentDialogButton.Primary,
                Content = scroller,
                XamlRoot = Root.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            dialog.Opened += (_, _) => PlayWindowsPopupOpen(dialog);
            ContentDialogResult result = await dialog.ShowAsync();
            Root.Focus(FocusState.Programmatic);
            if (launchWizard)
            {
                await ShowAmpWizardAsync().ConfigureAwait(true);
                return;
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            SimpleSettings previous = WriteSettingsFromPage(page);
            await ApplySettingsEffectsAsync(previous).ConfigureAwait(true);
        }
        finally
        {
            _settingsDialogOpen = false;
        }
    }

    /// <summary>
    /// Win11 popup: fade in, rise a little, scale 0.94 → 1. The smoke already
    /// fades; this is the card. Composition Offset is not used (it steals layout).
    /// </summary>
    private void PlayWindowsPopupOpen(ContentDialog dialog)
    {
        if (_reduceMotion)
        {
            return;
        }

        FrameworkElement card = FindNamedDescendant(dialog, "BackgroundElement") as FrameworkElement ?? dialog;
        card.Opacity = 0;
        card.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        CompositeTransform transform = new()
        {
            ScaleX = 0.94,
            ScaleY = 0.94,
            TranslateY = 20,
        };
        card.RenderTransform = transform;
        Duration duration = new(TimeSpan.FromMilliseconds(250));
        CubicEase ease = new() { EasingMode = EasingMode.EaseOut };
        Storyboard storyboard = new();
        storyboard.Children.Add(PopupOpenAnimation(card, "Opacity", 1, duration, ease));
        storyboard.Children.Add(PopupOpenAnimation(transform, "ScaleX", 1, duration, ease));
        storyboard.Children.Add(PopupOpenAnimation(transform, "ScaleY", 1, duration, ease));
        storyboard.Children.Add(PopupOpenAnimation(transform, "TranslateY", 0, duration, ease));
        storyboard.Begin();
    }

    private static DoubleAnimation PopupOpenAnimation(
        DependencyObject target,
        string property,
        double to,
        Duration duration,
        EasingFunctionBase ease)
    {
        DoubleAnimation animation = new()
        {
            To = to,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    private static DependencyObject? FindNamedDescendant(DependencyObject parent, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: { } found } && found == name)
            {
                return child;
            }

            if (FindNamedDescendant(child, name) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    private SettingsPageControls BuildSettingsContent(Panel host, Action? onAmpWizard = null)
    {
        ToggleSwitch remember = Toggle(_settings.RememberPlaybackPosition);
        ToggleSwitch matchRefresh = Toggle(_settings.MatchDisplayRefresh);
        ToggleSwitch autoHdr = Toggle(_settings.AutoEnableWindowsHdr);
        ToggleSwitch thumbs = Toggle(_settings.SeekThumbnails);
        ToggleSwitch gamepad = Toggle(_settings.GamepadEnabled);
        ToggleSwitch progressLine = Toggle(_settings.FullscreenProgressLine);
        ToggleSwitch passthrough = Toggle(_settings.AudioPassthrough);
        ToggleSwitch hardwareDecoding = Toggle(_settings.HardwareDecoding);
        ComboBox quality = new() { MinWidth = 160 };
        quality.Items.Add(_ui.QualityFast);
        quality.Items.Add(_ui.QualityBalanced);
        quality.Items.Add(_ui.QualityHigh);
        quality.SelectedIndex = _settings.Quality switch
        {
            QualityPreset.Fast => 0,
            QualityPreset.High => 2,
            _ => 1,
        };
        ToggleSwitch associate = Toggle(FileAssociation.IsRegistered());
        ComboBox language = new() { MinWidth = 160 };
        language.Items.Add("中文");
        language.Items.Add("English");
        language.SelectedIndex = UiStrings.IsEnglish(_settings.Language) ? 1 : 0;
        ComboBox encoding = new() { MinWidth = 160 };
        encoding.Items.Add("auto");
        encoding.Items.Add("utf-8");
        encoding.Items.Add("gbk");
        encoding.Items.Add("gb18030");
        encoding.Items.Add("big5");
        encoding.Items.Add("shift-jis");

        encoding.SelectedItem = _settings.SubCodepage;
        if (encoding.SelectedIndex < 0)
        {
            encoding.SelectedIndex = 0;
        }

        ComboBox ass = new() { MinWidth = 160 };
        ass.Items.Add("no");
        ass.Items.Add("scale");
        ass.Items.Add("yes");
        ass.SelectedItem = _settings.SubAssOverride;
        if (ass.SelectedIndex < 0)
        {
            ass.SelectedIndex = 0;
        }

        Button importConf = new() { Content = _ui.ImportMpvConf };
        TextBlock importStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        importConf.Click += async (_, _) =>
        {
            string? message = await ImportMpvConfAsync().ConfigureAwait(true);
            importStatus.Text = message ?? "";
        };
        Button ampWizard = new() { Content = _ui.AmpWizard };
        ampWizard.Click += async (_, _) =>
        {
            if (onAmpWizard is not null)
            {
                onAmpWizard();
                return;
            }

            await ShowAmpWizardAsync().ConfigureAwait(true);
        };
        Button checkUpdates = new() { Content = _ui.CheckUpdates };
        TextBlock updateStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        checkUpdates.Click += async (_, _) =>
        {
            checkUpdates.IsEnabled = false;
            try
            {
                updateStatus.Text = await CheckUpdatesAsync().ConfigureAwait(true);
            }
            finally
            {
                checkUpdates.IsEnabled = true;
            }
        };
        StackPanel advancedActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        advancedActions.Children.Add(importConf);
        advancedActions.Children.Add(importStatus);
        importStatus.VerticalAlignment = VerticalAlignment.Center;
        StackPanel updateActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        updateActions.Children.Add(checkUpdates);
        updateActions.Children.Add(updateStatus);
        updateStatus.VerticalAlignment = VerticalAlignment.Center;

        host.Children.Clear();
        host.Children.Add(SettingsSection(
            _ui.Settings,
            SettingsRow(_ui.Language, language),
            SettingsRow(_ui.RememberPosition, remember),
            SettingsRow(_ui.GamepadEnabled, gamepad),
            SettingsRow(_ui.RegisterDefault, associate)));
        host.Children.Add(SettingsSection(
            _ui.Video,
            SettingsRow(_ui.PictureQuality, quality),
            SettingsRow(_ui.HardwareDecoding, hardwareDecoding, _ui.HardwareDecodingHint),
            SettingsRow(_ui.MatchDisplayRefresh, matchRefresh),
            SettingsRow(_ui.AutoEnableWindowsHdr, autoHdr),
            SettingsRow(_ui.SeekThumbnails, thumbs),
            SettingsRow(_ui.FullscreenProgressLine, progressLine)));
        host.Children.Add(SettingsSection(
            _ui.Subtitles,
            SettingsRow(_ui.SubEncoding, encoding),
            SettingsRow(_ui.AssOverride, ass)));
        host.Children.Add(SettingsSection(
            _ui.AudioOutput,
            SettingsRow(_ui.AudioPassthrough, passthrough, _ui.AudioPassthroughHint),
            SettingsRow(_ui.AmpTitle, ampWizard)));
        host.Children.Add(SettingsSection(
            _ui.ImportMpvConf,
            SettingsBlock(advancedActions)));
        host.Children.Add(SettingsSection(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.Version, AppVersion.Display),
            SettingsBlock(updateActions),
            SettingsBlock(new TextBlock
            {
                Text = _ui.ShortcutsHint,
                Style = (Style)Application.Current.Resources["SecondaryText"],
                TextWrapping = TextWrapping.Wrap,
            })));

        return new SettingsPageControls
        {
            Remember = remember,
            MatchRefresh = matchRefresh,
            AutoHdr = autoHdr,
            Thumbs = thumbs,
            Gamepad = gamepad,
            Associate = associate,
            ProgressLine = progressLine,
            Passthrough = passthrough,
            HardwareDecoding = hardwareDecoding,
            Quality = quality,
            Language = language,
            Encoding = encoding,
            Ass = ass,
        };
    }

    /// <summary>Returns the settings as they were before the page was written back.</summary>
    private SimpleSettings WriteSettingsFromPage(SettingsPageControls page)
    {
        SimpleSettings previous = _settings;
        _settings = _settings with
        {
            RememberPlaybackPosition = page.Remember.IsOn,
            MatchDisplayRefresh = page.MatchRefresh.IsOn,
            AutoEnableWindowsHdr = page.AutoHdr.IsOn,
            SeekThumbnails = page.Thumbs.IsOn,
            GamepadEnabled = page.Gamepad.IsOn,
            Quality = page.Quality.SelectedIndex switch
            {
                0 => QualityPreset.Fast,
                2 => QualityPreset.High,
                _ => QualityPreset.Balanced,
            },
            SubCodepage = page.Encoding.SelectedItem as string ?? "auto",
            SubAssOverride = page.Ass.SelectedItem as string ?? "no",
            Language = page.Language.SelectedIndex == 1 ? "en" : "zh-CN",
            FullscreenProgressLine = page.ProgressLine.IsOn,
            AudioPassthrough = page.Passthrough.IsOn,
            HardwareDecoding = page.HardwareDecoding.IsOn,
        };
        if (page.Associate.IsOn)
        {
            FileAssociation.RegisterCurrentUser();
        }
        else
        {
            FileAssociation.UnregisterCurrentUser();
        }

        return previous;
    }

    private async Task ApplySettingsEffectsAsync(SimpleSettings previous)
    {
        ApplyLanguage();
        await ApplyQualityAsync().ConfigureAwait(true);
        await ApplySubtitleStyleAsync().ConfigureAwait(true);
        if (previous.HardwareDecoding != _settings.HardwareDecoding)
        {
            // A new hwdec value reinitialises the video decoder; leave it alone otherwise.
            await ApplyDecodingAsync().ConfigureAwait(true);
        }

        if (previous.AudioPassthrough != _settings.AudioPassthrough)
        {
            // Rebuilding the AO is not free; only touch audio when the toggle moved.
            await ApplyPassthroughChangedAsync().ConfigureAwait(true);
        }

        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private async Task CommitSettingsPageAsync()
    {
        if (_settingsPage is not { } page)
        {
            return;
        }

        SimpleSettings previous = WriteSettingsFromPage(page);
        await ApplySettingsEffectsAsync(previous).ConfigureAwait(true);
    }

    private async Task ShowAmpWizardAsync()
    {
        if (_engine is null)
        {
            ShowError(_ui.EngineNotReady);
            return;
        }

        string? raw = await _engine.GetPropertyStringAsync("audio-device-list").ConfigureAwait(true);
        IReadOnlyList<AudioDeviceInfo> devices = AmpGuide.ForOutputPicker(AudioDeviceListParser.Parse(raw));
        if (devices.Count == 0)
        {
            ShowError(_ui.NoAudioDevices);
            return;
        }

        RadioButtons sink = new();
        sink.Items.Add(_ui.AmpSinkSpeakers);
        sink.Items.Add(_ui.AmpSinkHdmi);
        sink.Items.Add(_ui.AmpSinkAuto);

        RadioButtons speakerMode = new();
        speakerMode.Items.Add(_ui.AmpFollowWindows);
        speakerMode.Items.Add(_ui.AmpForceStereo);
        speakerMode.SelectedIndex = _audioPolicy == AudioPolicy.ForceStereo ? 1 : 0;

        RadioButtons hdmiMode = new();
        hdmiMode.Items.Add(_ui.AmpBitstream);
        hdmiMode.Items.Add(_ui.AmpHtPcm);
        // HDMI defaults to bitstream unless the user already chose PCM without it.
        hdmiMode.SelectedIndex = !_settings.AudioPassthrough && _audioPolicy == AudioPolicy.HomeTheaterPcm ? 1 : 0;

        ComboBox deviceBox = new() { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (AudioDeviceInfo info in devices)
        {
            string mark = AmpGuide.Classify(info) switch
            {
                AudioSinkKind.Hdmi => _ui.MarkHdmi,
                AudioSinkKind.Speakers => _ui.MarkSpeakers,
                AudioSinkKind.Auto => _ui.MarkAuto,
                _ => "",
            };
            string label = string.IsNullOrWhiteSpace(info.Description) ? info.Name : info.Description;
            deviceBox.Items.Add(new ComboBoxItem { Content = mark + label, Tag = info.Name });
        }

        TextBlock note = new()
        {
            FontSize = 12,
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
        };

        void SelectDevice(string? name)
        {
            for (int i = 0; i < deviceBox.Items.Count; i++)
            {
                if (deviceBox.Items[i] is ComboBoxItem { Tag: string tag }
                    && tag.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    deviceBox.SelectedIndex = i;
                    return;
                }
            }

            if (deviceBox.SelectedIndex < 0 && deviceBox.Items.Count > 0)
            {
                deviceBox.SelectedIndex = 0;
            }
        }

        AudioSinkKind Kind() => sink.SelectedIndex switch
        {
            0 => AudioSinkKind.Speakers,
            1 => AudioSinkKind.Hdmi,
            _ => AudioSinkKind.Auto,
        };

        void SyncUi()
        {
            AudioSinkKind kind = Kind();
            speakerMode.Visibility = kind == AudioSinkKind.Speakers ? Visibility.Visible : Visibility.Collapsed;
            hdmiMode.Visibility = kind == AudioSinkKind.Hdmi ? Visibility.Visible : Visibility.Collapsed;
            AudioDeviceInfo? prefer = AmpGuide.Prefer(devices, kind);
            SelectDevice(prefer?.Name);
            AmpRecommendation recommendation = AmpGuide.Recommend(
                kind,
                passthrough: kind == AudioSinkKind.Hdmi && hdmiMode.SelectedIndex == 0,
                forceStereo: kind == AudioSinkKind.Speakers && speakerMode.SelectedIndex == 1);
            note.Text = AmpWizardNote(kind, recommendation);
        }

        sink.SelectionChanged += (_, _) => SyncUi();
        speakerMode.SelectionChanged += (_, _) =>
        {
            note.Text = AmpWizardNote(Kind(), AmpGuide.Recommend(
                Kind(),
                passthrough: false,
                forceStereo: speakerMode.SelectedIndex == 1));
        };
        hdmiMode.SelectionChanged += (_, _) =>
        {
            note.Text = AmpWizardNote(Kind(), AmpGuide.Recommend(
                Kind(),
                passthrough: hdmiMode.SelectedIndex == 0));
        };

        AudioSinkKind initial = AudioSinkKind.Auto;
        if (!string.IsNullOrWhiteSpace(_settings.AudioDevice))
        {
            AudioDeviceInfo? pinned = devices.FirstOrDefault(info =>
                info.Name.Equals(_settings.AudioDevice, StringComparison.OrdinalIgnoreCase));
            if (pinned is not null)
            {
                initial = AmpGuide.Classify(pinned);
            }
        }

        sink.SelectedIndex = initial switch
        {
            AudioSinkKind.Speakers => 0,
            AudioSinkKind.Hdmi => 1,
            _ => 2,
        };
        SelectDevice(_settings.AudioDevice ?? AmpGuide.Prefer(devices, Kind())?.Name);

        StackPanel panel = new() { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = _ui.AmpWhere });
        panel.Children.Add(sink);
        panel.Children.Add(speakerMode);
        panel.Children.Add(hdmiMode);
        panel.Children.Add(new TextBlock { Text = _ui.AmpDevice });
        panel.Children.Add(deviceBox);
        panel.Children.Add(note);
        ContentDialog dialog = new()
        {
            Title = _ui.AmpTitle,
            PrimaryButtonText = _ui.Apply,
            CloseButtonText = _ui.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 480,
                MaxWidth = 480,
            },
            XamlRoot = Root.XamlRoot,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        Root.Focus(FocusState.Programmatic);
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        AudioSinkKind kind = Kind();
        AmpRecommendation recommendation = AmpGuide.Recommend(
            kind,
            passthrough: kind == AudioSinkKind.Hdmi && hdmiMode.SelectedIndex == 0,
            forceStereo: kind == AudioSinkKind.Speakers && speakerMode.SelectedIndex == 1);
        string device = deviceBox.SelectedItem is ComboBoxItem { Tag: string name } && !string.IsNullOrWhiteSpace(name)
            ? name
            : "auto";
        await ApplyAmpGuideAsync(recommendation, device).ConfigureAwait(true);
    }

    private async Task ApplyAmpGuideAsync(AmpRecommendation recommendation, string device)
    {
        _settings = _settings with
        {
            AudioPolicy = recommendation.Policy,
            AudioPassthrough = recommendation.Passthrough,
            AudioDevice = device,
        };
        _audioPolicy = recommendation.Policy;
        await ApplyPlaybackPolicyAsync().ConfigureAwait(true);
        await SettleAudioAsync().ConfigureAwait(true);
        ShowOsd(recommendation.Passthrough
            ? _ui.AudioPassthrough + "  " + DeviceLabel(device)
            : DeviceLabel(device));
    }

    private string AmpWizardNote(AudioSinkKind kind, AmpRecommendation recommendation)
    {
        if (recommendation.Passthrough)
        {
            return _ui.AmpNoteBitstream;
        }

        if (kind == AudioSinkKind.Speakers)
        {
            return _ui.AmpNoteSpeakers;
        }

        if (recommendation.Policy == AudioPolicy.HomeTheaterPcm)
        {
            return _ui.AmpNoteHtPcm;
        }

        return _ui.AmpNoteAuto;
    }

    private string DeviceLabel(string device)
    {
        if (device.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return _ui.DeviceAuto;
        }

        int slash = device.IndexOf('{', StringComparison.Ordinal);
        return slash > 0 ? _ui.DevicePinned : device;
    }

    /// <summary>
    /// Asks GitHub Releases for the latest tag and reports it. Only run when the user clicks
    /// the button; the app never applies updates itself.
    /// </summary>
    private async Task<string> CheckUpdatesAsync()
    {
        string current = AppVersion.Display;
        try
        {
            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(AppVersion.Name + "/" + current);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            string json = await http.GetStringAsync(new Uri(AppRelease.LatestReleaseApi)).ConfigureAwait(true);
            if (!AppRelease.TryReadTag(json, out string tag))
            {
                Log.Warning("Update check: no tag_name in the release document");
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.UpdateFailed, AppRelease.LatestReleaseApi);
            }

            UpdateDecision compared = AppRelease.CompareRemote(current, tag);
            Log.Information("Update check: latest {Tag}, current {Current}, {Status}", tag, current, compared.Status);
            return compared.Status switch
            {
                UpdateStatus.Available => string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    _ui.UpdateAvailable,
                    compared.Remote,
                    current,
                    AppRelease.LatestReleasePage),
                UpdateStatus.UpToDate => string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.UpdateLatest, current),
                _ => string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.UpdateFailed, tag),
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update check failed");
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.UpdateFailed, ex.Message);
        }
    }

    private async Task<string?> ImportMpvConfAsync()
    {
        if (_engine is null)
        {
            return _ui.EngineNotReady;
        }

        FileOpenPicker picker = new();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".conf");
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add("*");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return null;
        }

        string text = await FileIO.ReadTextAsync(file);
        MpvConfParseResult parsed = MpvConfParser.Parse(text);
        if (parsed.Accepted.Count > 0)
        {
            await _engine.ApplyPropertiesAsync(parsed.Accepted).ConfigureAwait(true);
        }

        int rejected = parsed.Rejected.Count;
        return rejected == 0
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, _ui.ImportApplied, parsed.Accepted.Count)
            : string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                _ui.ImportRejected,
                parsed.Accepted.Count,
                rejected);
    }

    private string FormatTrack(TrackInfo track)
    {
        string mark = track.Selected ? "● " : "○ ";
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(track.Language))
        {
            parts.Add(track.Language);
        }

        if (!string.IsNullOrWhiteSpace(track.Title))
        {
            parts.Add(track.Title);
        }

        string body = parts.Count > 0 ? string.Join(" · ", parts) : "#" + track.Id;
        return mark + body + (track.External ? _ui.ExternalMark : "");
    }

    private static string Format(TimeSpan? value)
    {
        if (value is null)
        {
            return "00:00";
        }

        TimeSpan t = value.Value;
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }
}
