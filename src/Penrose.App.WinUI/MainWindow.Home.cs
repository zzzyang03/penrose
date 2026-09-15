using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Penrose.Core.Settings;
using Penrose.Core.Sources;
using Penrose.Sources.Emby;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;
using Windows.Foundation;
using Windows.Graphics;

namespace Penrose.App.WinUI;

/// <summary>
/// Home page: left column (brand, or the saved media servers), right column
/// (open actions and recent files, or the add-server form), and the server
/// connection lifecycle (connect, restore on start, open, remove).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Server whose saved token turned out invalid; the form is pre-filled for it.</summary>
    private LibraryServerSettings? _serverBeingEdited;
    private SettingsPageControls? _settingsPage;
    private bool _settingsBusy;
    private bool _settingsDialogOpen;
    private bool _serverFormBusy;
    private bool _absorbingUrl;
    private bool _homeFitted;

    private void WireHome()
    {
        ServersButton.Click += (_, _) => ShowServersPanel(true);
        HomeSettingsButton.Click += (_, _) => ShowSettingsPage();
        SettingsBackButton.Click += async (_, _) => await HideSettingsPageAsync().ConfigureAwait(true);
        ServersBackButton.Click += (_, _) => ShowServersPanel(false);
        AddServerButton.Click += async (_, _) =>
        {
            await HideSettingsPageAsync().ConfigureAwait(true);
            ShowServerForm(null);
        };
        ServerList.ItemClick += async (_, e) =>
        {
            if (e.ClickedItem is ServerRow row)
            {
                await OpenServerAsync(row.Server).ConfigureAwait(true);
            }
        };
        ServerCancelButton.Click += (_, _) => HideServerForm();
        ServerTestButton.Click += async (_, _) => await TestServerFormAsync().ConfigureAwait(true);
        ServerConnectButton.Click += async (_, _) => await ConnectServerFormAsync().ConfigureAwait(true);
        ServerPasswordBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await ConnectServerFormAsync().ConfigureAwait(true);
            }
        };
        ServerHttpsBox.Checked += (_, _) => SwapDefaultPort(https: true);
        ServerHttpsBox.Unchecked += (_, _) => SwapDefaultPort(https: false);
        ServerHostBox.TextChanged += (_, _) => AbsorbPastedUrl();
        ServerPortBox.TextChanged += (_, _) => InferHttpsFromPort();
        ResumeLastButton.Click += async (_, _) =>
        {
            if (ResumeLastButton.Tag is string path)
            {
                await PlayPathAsync(path).ConfigureAwait(true);
            }
        };
        ServerKindBox.Items.Add(new ComboBoxItem { Content = "Emby", Tag = "emby" });
        ServerKindBox.SelectedIndex = 0;
        EmptyState.Loaded += (_, _) => FitWindowToHome();
        HomeRight.SizeChanged += (_, _) => ClipToBounds(HomeRight);
    }

    private void ApplyHomeLanguage()
    {
        BrandTitle.Text = AppVersion.Name;
        BrandVersion.Text = string.Format(CultureInfo.InvariantCulture, _ui.VersionShort, AppVersion.Display);
        ServersTitle.Text = _ui.MediaServers;
        ServersButtonText.Text = _ui.MediaServers;
        SetName(ServersButton, _ui.MediaServers);
        HomeSettingsText.Text = _ui.Settings;
        SetName(HomeSettingsButton, _ui.Settings);
        SettingsTitle.Text = _ui.Settings;
        SettingsBackText.Text = _ui.BackNav;
        SetName(SettingsBackButton, _ui.BackNav);
        ServersEmpty.Text = _ui.MediaServersEmpty;
        AddServerText.Text = _ui.AddServer;
        ServerFormTitle.Text = _ui.AddServer;
        ServerNameBox.Header = _ui.ServerName;
        ServerNameBox.PlaceholderText = _ui.MediaServers;
        ServerKindBox.Header = _ui.ServerKind;
        ServerHostBox.Header = _ui.ServerHost;
        ServerHostBox.PlaceholderText = "emby.example.com";
        ServerPortBox.Header = _ui.ServerPort;
        ServerHttpsBox.Content = _ui.ServerHttps;
        ServerUserBox.Header = _ui.EmbyUser;
        ServerPasswordBox.Header = _ui.EmbyPassword;
        ServerTestText.Text = _ui.EmbyTestConnection;
        ServerConnectText.Text = _ui.Connect;
        ServerCancelText.Text = _ui.Cancel;
        SetName(ServersBackButton, _ui.Back);
        ToolTipService.SetToolTip(ServersBackButton, _ui.Back);
        SetName(AddServerButton, _ui.AddServer);
        SetName(ServerTestButton, _ui.EmbyTestConnection);
        SetName(ServerConnectButton, _ui.Connect);
        SetName(ServerCancelButton, _ui.Cancel);
        SetName(EmptyOpenFile, _ui.OpenFile);
        SetName(EmptyOpenUrl, _ui.OpenUrl);
        SetName(EmptyOpenFolder, _ui.OpenFolder);
        RefreshServerList();
    }

    // ---- panels -------------------------------------------------------------

    private void ShowServersPanel(bool open)
    {
        BrandPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        ServersPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open)
        {
            RefreshServerList();
        }
        else
        {
            HideServerForm();
            // Opened over a playing video from the library page: give the video back.
            if (_currentUri is not null)
            {
                EmptyState.Visibility = Visibility.Collapsed;
                UpdateTransportVisibility();
            }
        }
    }

    /// <summary>The library page's account button: back home with the server list open.</summary>
    private void ShowServersHome()
    {
        HideEmbyPage();
        EmptyState.Visibility = Visibility.Visible;
        UpdateTransportVisibility();
        ShowServersPanel(true);
    }

    private void ShowServerForm(LibraryServerSettings? server)
    {
        if (_settingsPage is { } page)
        {
            SimpleSettings previous = WriteSettingsFromPage(page);
            SettingsHost.Children.Clear();
            _settingsPage = null;
            SettingsPanel.Visibility = Visibility.Collapsed;
            ResetPageVisual(SettingsPanel);
            _ = ApplySettingsEffectsAsync(previous);
        }

        _serverBeingEdited = server;
        ServerFormTitle.Text = server is null ? _ui.AddServer : _ui.EditServer;
        ServerNameBox.Text = server?.Name ?? "";
        ServerUserBox.Text = server?.UserName ?? "";
        ServerPasswordBox.Password = "";
        ServerFormStatus.Text = "";
        if (server is not null && Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out Uri? url))
        {
            ServerHttpsBox.IsChecked = url.Scheme == Uri.UriSchemeHttps;
            ServerHostBox.Text = url.Host + (url.AbsolutePath.Length > 1 ? url.AbsolutePath.TrimEnd('/') : "");
            ServerPortBox.Text = url.Port.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            ServerHttpsBox.IsChecked = false;
            ServerHostBox.Text = "";
            ServerPortBox.Text = "8096";
        }

        OpenPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        ServerFormPanel.Visibility = Visibility.Visible;
        Control first = string.IsNullOrEmpty(ServerHostBox.Text) ? ServerHostBox : ServerPasswordBox;
        first.Focus(FocusState.Programmatic);
    }

    private void HideServerForm()
    {
        _serverBeingEdited = null;
        ServerFormPanel.Visibility = Visibility.Collapsed;
        if (SettingsPanel.Visibility != Visibility.Visible)
        {
            OpenPanel.Visibility = Visibility.Visible;
        }
    }

    private bool IsServerFormOpen => ServerFormPanel.Visibility == Visibility.Visible;

    private bool IsSettingsOpen => SettingsPanel.Visibility == Visibility.Visible;

    /// <summary>Home uses the right-column page; playback keeps a dialog so video continues.</summary>
    private void OpenSettings()
    {
        if (EmptyState.Visibility == Visibility.Visible)
        {
            ShowSettingsPage();
            return;
        }

        _ = ShowSettingsDialogAsync();
    }

    /// <summary>Settings lives in the home right column, not a dialog.</summary>
    private void ShowSettingsPage()
    {
        if (IsSettingsOpen || _settingsBusy)
        {
            return;
        }

        HideEmbyPage();
        EmptyState.Visibility = Visibility.Visible;
        UpdateTransportVisibility();
        _serverBeingEdited = null;
        if (_settingsPage is null)
        {
            PopulateSettingsPage();
        }

        _ = OpenSettingsRevealAsync();
    }

    private async Task HideSettingsPageAsync()
    {
        if (_settingsBusy || (!IsSettingsOpen && _settingsPage is null))
        {
            return;
        }

        _settingsBusy = true;
        try
        {
            await CommitSettingsPageAsync().ConfigureAwait(true);
            await CloseSettingsRevealAsync().ConfigureAwait(true);
            SettingsHost.Children.Clear();
            _settingsPage = null;
        }
        finally
        {
            _settingsBusy = false;
        }
    }

    private async Task OpenSettingsRevealAsync()
    {
        FrameworkElement outgoing = ServerFormPanel.Visibility == Visibility.Visible
            ? ServerFormPanel
            : OpenPanel;
        SettingsPanel.Visibility = Visibility.Visible;
        SettingsPanel.UpdateLayout();
        if (_reduceMotion)
        {
            ResetPageVisual(SettingsPanel);
            ResetPageVisual(outgoing);
            outgoing.Visibility = Visibility.Collapsed;
            return;
        }

        await AnimateSettingsNavAsync(SettingsPanel, outgoing, forward: true).ConfigureAwait(true);
        outgoing.Visibility = Visibility.Collapsed;
        ResetPageVisual(outgoing);
    }

    private async Task CloseSettingsRevealAsync()
    {
        FrameworkElement incoming = IsServerFormOpen ? ServerFormPanel : OpenPanel;
        incoming.Visibility = Visibility.Visible;
        incoming.UpdateLayout();
        if (_reduceMotion)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            ResetPageVisual(SettingsPanel);
            ResetPageVisual(incoming);
            return;
        }

        await AnimateSettingsNavAsync(incoming, SettingsPanel, forward: false).ConfigureAwait(true);
        SettingsPanel.Visibility = Visibility.Collapsed;
        ResetPageVisual(SettingsPanel);
        ResetPageVisual(incoming);
    }

    /// <summary>
    /// Windows Settings drill-in / back. TranslateTransform is additive, so
    /// layout (the 64-dip title-bar inset) is not overwritten — Composition
    /// Offset was, which parked the back button under the caption on the
    /// second open.
    /// </summary>
    private Task AnimateSettingsNavAsync(UIElement incoming, UIElement outgoing, bool forward)
    {
        RestoreLayoutOffset(incoming);
        RestoreLayoutOffset(outgoing);

        double slide = 80;
        TranslateTransform enter = Slide(incoming);
        TranslateTransform leave = Slide(outgoing);
        incoming.Opacity = 0;
        enter.X = forward ? slide : -slide;
        outgoing.Opacity = 1;
        leave.X = 0;

        Duration duration = new(TimeSpan.FromMilliseconds(350));
        CubicEase ease = new() { EasingMode = EasingMode.EaseOut };
        Storyboard storyboard = new();
        storyboard.Children.Add(NavSlideAnimation(incoming, "Opacity", 1, duration, ease));
        storyboard.Children.Add(NavSlideAnimation(enter, "X", 0, duration, ease));
        storyboard.Children.Add(NavSlideAnimation(outgoing, "Opacity", 0, duration, ease));
        storyboard.Children.Add(NavSlideAnimation(leave, "X", forward ? -40 : slide, duration, ease));

        TaskCompletionSource done = new();
        storyboard.Completed += (_, _) => done.TrySetResult();
        storyboard.Begin();
        return done.Task;
    }

    private static DoubleAnimation NavSlideAnimation(
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

    private static void ResetPageVisual(UIElement element)
    {
        element.Opacity = 1;
        Slide(element).X = 0;
        RestoreLayoutOffset(element);
    }

    /// <summary>
    /// Composition Offset replaces the layout slot. After an older animation
    /// wrote (0,0), put the visual back on the arranged position.
    /// </summary>
    private static void RestoreLayoutOffset(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Offset");
        visual.StopAnimation("Opacity");
        visual.Clip = null;
        visual.Opacity = 1;
        if (element is FrameworkElement fe)
        {
            visual.Offset = new Vector3((float)fe.ActualOffset.X, (float)fe.ActualOffset.Y, 0);
        }
    }

    private static void ClipToBounds(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        element.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, element.ActualWidth, element.ActualHeight),
        };
    }

    private void SwapDefaultPort(bool https)
    {
        // Emby's stock ports; anything the user typed themselves is left alone.
        if (https && ServerPortBox.Text.Trim() == "8096")
        {
            ServerPortBox.Text = "8920";
        }
        else if (!https && ServerPortBox.Text.Trim() == "8920")
        {
            ServerPortBox.Text = "8096";
        }
    }

    /// <summary>
    /// A URL typed or pasted into the address box is split into the fields: the
    /// scheme becomes the HTTPS box, the port goes to the port box, the host (and
    /// any path) stays. Runs as the text changes, so "https://" alone already
    /// ticks the box and disappears from the address.
    /// </summary>
    private void AbsorbPastedUrl()
    {
        string text = ServerHostBox.Text.Trim();
        int marker = text.IndexOf("://", StringComparison.Ordinal);
        if (marker < 0 || _absorbingUrl)
        {
            return;
        }

        _absorbingUrl = true;
        try
        {
            string scheme = text[..marker].Trim().ToLowerInvariant();
            if (scheme == "https")
            {
                ServerHttpsBox.IsChecked = true;
            }
            else if (scheme == "http")
            {
                ServerHttpsBox.IsChecked = false;
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out Uri? url)
                && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
                && url.Host.Length > 0)
            {
                ServerPortBox.Text = url.Port.ToString(CultureInfo.InvariantCulture);
                ServerHostBox.Text = url.Host + (url.AbsolutePath.Length > 1 ? url.AbsolutePath.TrimEnd('/') : "");
            }
            else
            {
                // Still being typed ("https://emb"): the scheme is absorbed, the rest stays.
                ServerHostBox.Text = text[(marker + 3)..].TrimStart('/');
            }

            ServerHostBox.SelectionStart = ServerHostBox.Text.Length;
        }
        finally
        {
            _absorbingUrl = false;
        }
    }

    /// <summary>443 and Emby's 8920 are HTTPS ports; typing one ticks the box (never the reverse).</summary>
    private void InferHttpsFromPort()
    {
        if (ServerPortBox.Text.Trim() is "443" or "8920")
        {
            ServerHttpsBox.IsChecked = true;
        }
    }

    /// <summary>
    /// Plain HTTP sent to an HTTPS endpoint comes back as 400 (or a broken
    /// connection) rather than a clear error. When that happens, the same host
    /// and port are tried over HTTPS once; success ticks the box and wins.
    /// </summary>
    private async Task<Uri> ResolveSchemeAsync(Uri url)
    {
        if (url.Scheme == Uri.UriSchemeHttps)
        {
            return url;
        }

        try
        {
            await EmbyClient.GetPublicInfoAsync(url).ConfigureAwait(true);
            return url;
        }
        catch (EmbyHttpException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
        }
        catch (System.Net.Http.HttpRequestException)
        {
        }
        catch (System.IO.IOException)
        {
        }

        Uri https = new UriBuilder(url) { Scheme = Uri.UriSchemeHttps, Port = url.Port }.Uri;
        try
        {
            await EmbyClient.GetPublicInfoAsync(https).ConfigureAwait(true);
            ServerHttpsBox.IsChecked = true;
            Log.Information("Emby: {Host}:{Port} answers over HTTPS only; switched", url.Host, url.Port);
            return https;
        }
        catch (Exception ex) when (ex is EmbyHttpException or System.Net.Http.HttpRequestException or System.IO.IOException or TaskCanceledException)
        {
            return url; // let the original attempt report its own error
        }
    }

    /// <summary>The form as a server entry (no secrets) plus the base URL it describes, or an error.</summary>
    private (LibraryServerSettings? Draft, Uri? Url, string? Error) ReadServerForm()
    {
        // A pasted URL may still be sitting in the address box (no focus change yet).
        AbsorbPastedUrl();
        string host = ServerHostBox.Text.Trim().TrimEnd('/');
        string user = ServerUserBox.Text.Trim();
        if (host.Length == 0 || user.Length == 0)
        {
            return (null, null, _ui.ServerFillHostUser);
        }

        bool https = ServerHttpsBox.IsChecked == true;
        string portText = ServerPortBox.Text.Trim();
        string path = "";
        int slash = host.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            path = host[slash..].TrimEnd('/');
            host = host[..slash];
        }

        // "host:8096" typed into the address wins over the port box.
        int colon = host.LastIndexOf(':');
        if (colon > 0 && !host.Contains(']', StringComparison.Ordinal) && int.TryParse(host[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int inline))
        {
            portText = inline.ToString(CultureInfo.InvariantCulture);
            host = host[..colon];
            ServerHostBox.Text = host;
            ServerPortBox.Text = portText;
        }

        int port = int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p is > 0 and < 65536
            ? p
            : https ? 443 : 8096;

        Uri url;
        try
        {
            url = new UriBuilder(https ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, host, port, path + "/").Uri;
        }
        catch (UriFormatException)
        {
            return (null, null, string.Format(CultureInfo.InvariantCulture, _ui.ConnectFailed, "URL"));
        }

        string kind = (ServerKindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "emby";
        LibraryServerSettings draft = (_serverBeingEdited ?? new LibraryServerSettings()) with
        {
            Name = ServerNameBox.Text.Trim(),
            Kind = kind,
            BaseUrl = url.AbsoluteUri,
            UserName = user,
        };
        return (draft, url, null);
    }

    private async Task TestServerFormAsync()
    {
        if (_serverFormBusy)
        {
            return;
        }

        (LibraryServerSettings? draft, Uri? url, string? error) = ReadServerForm();
        if (draft is null || url is null)
        {
            ServerFormStatus.Text = error;
            return;
        }

        _serverFormBusy = true;
        ServerFormStatus.Text = _ui.EmbyTesting;
        try
        {
            url = await ResolveSchemeAsync(url).ConfigureAwait(true);
            ServerFormStatus.Text = await TestEmbyConnectionAsync(url.AbsoluteUri, draft.UserName, ServerPasswordBox.Password).ConfigureAwait(true);
        }
        finally
        {
            _serverFormBusy = false;
        }
    }

    private async Task ConnectServerFormAsync()
    {
        if (_serverFormBusy)
        {
            return;
        }

        (LibraryServerSettings? draft, Uri? url, string? error) = ReadServerForm();
        if (draft is null || url is null)
        {
            ServerFormStatus.Text = error;
            return;
        }

        _serverFormBusy = true;
        ServerFormStatus.Text = _ui.EmbyConnecting;
        try
        {
            Uri resolved = await ResolveSchemeAsync(url).ConfigureAwait(true);
            if (resolved != url)
            {
                url = resolved;
                draft = draft with { BaseUrl = url.AbsoluteUri };
            }

            (bool ok, string message) = await ConnectServerAsync(draft, url, ServerPasswordBox.Password).ConfigureAwait(true);
            ServerFormStatus.Text = message;
            if (ok)
            {
                HideServerForm();
                ShowServersPanel(false);
                await ShowEmbyPageAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            _serverFormBusy = false;
        }
    }

    // ---- server list --------------------------------------------------------

    private void RefreshServerList()
    {
        IReadOnlyList<LibraryServerSettings> servers = _settings.Servers;
        ServerList.ItemsSource = servers
            .Select(s => new ServerRow(s, _emby is not null && s.Id == _settings.ActiveServerId, _ui.ServerConnected, _ui.RemoveServer))
            .ToList();
        ServersEmpty.Visibility = servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ServerList.Visibility = servers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnRemoveServerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryServerSettings server })
        {
            return;
        }

        try
        {
            await RemoveServerAsync(server).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Remove server failed");
        }
    }

    // ---- connection lifecycle ----------------------------------------------

    private static string EmbySecretKey(Uri url) => "emby:" + url.AbsoluteUri.TrimEnd('/');

    private static EmbyClient NewEmbyClient(Uri url) =>
        new(url, Environment.MachineName, "penrose-" + Environment.MachineName, AppVersion.Display);

    /// <summary>Signs in, stores the token, saves / updates the entry and makes it the active library.</summary>
    private async Task<(bool Ok, string Message)> ConnectServerAsync(LibraryServerSettings draft, Uri url, string password)
    {
        EmbyClient? client = NewEmbyClient(url);
        try
        {
            EmbySession session = await client.AuthenticateAsync(draft.UserName, password ?? "").ConfigureAwait(true);
            await DetachServerAsync().ConfigureAwait(true);
            await _secrets.SaveAsync(EmbySecretKey(url), session.AccessToken).ConfigureAwait(true);
            // The same account on the same server is one entry, however it was typed in.
            LibraryServerSettings? same = _settings.Servers.FirstOrDefault(s =>
                s.Id != draft.Id
                && string.Equals(s.BaseUrl.TrimEnd('/'), draft.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.UserName, session.UserName, StringComparison.OrdinalIgnoreCase));
            LibraryServerSettings saved = draft with
            {
                Id = same?.Id ?? draft.Id,
                Name = draft.Name.Length > 0 ? draft.Name : same?.Name ?? "",
                UserName = session.UserName,
                UserId = session.UserId,
            };
            List<LibraryServerSettings> servers = _settings.Servers.Where(s => s.Id != saved.Id).ToList();
            servers.Insert(0, saved);
            _settings = _settings with { Servers = servers, ActiveServerId = saved.Id };
            await SaveSettingsAsync().ConfigureAwait(true);
            AttachEmby(client, session.UserId);
            client = null; // owned by the window now
            RefreshServerList();
            Log.Information("Emby: connected to {Host} as {User} (server {Version})", url.Host, session.UserName, session.ServerVersion);
            return (true, string.Format(CultureInfo.InvariantCulture, _ui.ConnectedAs, session.UserName));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Emby connect failed");
            return (false, string.Format(CultureInfo.InvariantCulture, _ui.ConnectFailed, DescribeEmbyFailure(ex)));
        }
        finally
        {
            client?.Dispose();
        }
    }

    /// <summary>A saved server clicked: reuse its token, or ask for the password when there is none.</summary>
    private async Task OpenServerAsync(LibraryServerSettings server)
    {
        if (_emby is not null && server.Id == _settings.ActiveServerId)
        {
            await ShowEmbyPageAsync().ConfigureAwait(true);
            return;
        }

        if (!Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out Uri? url) || string.IsNullOrWhiteSpace(server.UserId))
        {
            ShowServerForm(server);
            return;
        }

        string? token = await _secrets.LoadAsync(EmbySecretKey(url)).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(token))
        {
            ShowServerForm(server);
            return;
        }

        await DetachServerAsync().ConfigureAwait(true);
        EmbyClient client = NewEmbyClient(url);
        client.SetAccessToken(token);
        AttachEmby(client, server.UserId);
        _settings = _settings with { ActiveServerId = server.Id };
        await SaveSettingsAsync().ConfigureAwait(true);
        RefreshServerList();
        await ShowEmbyPageAsync().ConfigureAwait(true);
    }

    /// <summary>On start: reconnect the active server with its saved token, silently.</summary>
    private async Task TryRestoreLibraryAsync()
    {
        LibraryServerSettings? server = _settings.ActiveServer;
        if (server is null
            || !server.Kind.Equals("emby", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(server.UserId)
            || !Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out Uri? url))
        {
            return;
        }

        string? token = await _secrets.LoadAsync(EmbySecretKey(url)).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try
        {
            EmbyClient client = NewEmbyClient(url);
            client.SetAccessToken(token);
            AttachEmby(client, server.UserId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Emby restore failed");
        }
    }

    private void AttachEmby(EmbyClient client, string userId)
    {
        if (!ReferenceEquals(_emby, client))
        {
            _emby?.Dispose();
        }

        _emby = client;
        _embyResolver = new EmbyPlaybackResolver(client, userId, _settings.AllowServerFilePaths);
        _embyLibrary = new EmbyContentProvider(client, userId);
        _session = new SessionReportCoordinator(new EmbyPlaybackReporter(client, userId));
    }

    /// <summary>Drops the live connection (session stop, client) but keeps the saved entry and token.</summary>
    private async Task DetachServerAsync()
    {
        await StopLibrarySessionAsync().ConfigureAwait(true);
        _emby?.Dispose();
        _emby = null;
        _embyResolver = null;
        _embyLibrary = null;
        _session = null;
        ResetLibraryNavigation();
        LibraryBackButton.Visibility = Visibility.Collapsed;
        HideEmbyPage();
    }

    /// <summary>Forgets a saved server: token, entry, and the connection if it is the active one.</summary>
    private async Task RemoveServerAsync(LibraryServerSettings server)
    {
        if (server.Id == _settings.ActiveServerId)
        {
            await DetachServerAsync().ConfigureAwait(true);
        }

        if (Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out Uri? url))
        {
            await _secrets.DeleteAsync(EmbySecretKey(url)).ConfigureAwait(true);
        }

        _settings = _settings with
        {
            Servers = _settings.Servers.Where(s => s.Id != server.Id).ToList(),
            ActiveServerId = _settings.ActiveServerId == server.Id ? null : _settings.ActiveServerId,
        };
        await SaveSettingsAsync().ConfigureAwait(true);
        RefreshServerList();
    }

    // ---- window size --------------------------------------------------------

    /// <summary>
    /// The first window is sized so the whole home page fits, whatever it holds:
    /// both columns are measured unconstrained and the client area grown to the
    /// larger of that and the previous size, within the display's work area.
    /// </summary>
    private void FitWindowToHome()
    {
        if (_homeFitted || _appWindow is null || _appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            return;
        }

        _homeFitted = true;
        try
        {
            Windows.Foundation.Size infinite = new(double.PositiveInfinity, double.PositiveInfinity);
            HomeLeft.Measure(infinite);
            HomeRight.Measure(infinite);
            double leftWidth = Math.Max(HomeLeft.DesiredSize.Width, 300);
            double rightWidth = Math.Max(HomeRight.DesiredSize.Width, 620);
            double contentHeight = Math.Max(HomeLeft.DesiredSize.Height, HomeRight.DesiredSize.Height);
            double wantWidth = leftWidth + rightWidth;
            double wantHeight = contentHeight + 8;
            HomeLeft.InvalidateMeasure();
            HomeRight.InvalidateMeasure();

            double scale = Root.XamlRoot?.RasterizationScale ?? 1;
            SizeInt32 client = _appWindow.ClientSize;
            int width = Math.Max(client.Width, (int)Math.Ceiling(wantWidth * scale));
            int height = Math.Max(client.Height, (int)Math.Ceiling(wantHeight * scale));
            RectInt32 work = Microsoft.UI.Windowing.DisplayArea
                .GetFromWindowId(_appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest)
                .WorkArea;
            width = Math.Min(width, (int)(work.Width * 0.92));
            height = Math.Min(height, (int)(work.Height * 0.9));
            if (width == client.Width && height == client.Height)
            {
                return;
            }

            _appWindow.ResizeClient(new SizeInt32(width, height));
            // Keep it on screen after growing.
            PointInt32 pos = _appWindow.Position;
            SizeInt32 outer = _appWindow.Size;
            int x = Math.Clamp(pos.X, work.X, Math.Max(work.X, work.X + work.Width - outer.Width));
            int y = Math.Clamp(pos.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - outer.Height));
            if (x != pos.X || y != pos.Y)
            {
                _appWindow.Move(new PointInt32(x, y));
            }

            Log.Information("Window: fitted to home {Width}x{Height} px (content {ContentW:0}x{ContentH:0} dip @ {Scale})", width, height, wantWidth, wantHeight, scale);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Window fit to home failed");
        }
    }

    private sealed class SettingsPageControls
    {
        public required ToggleSwitch Remember { get; init; }
        public required ToggleSwitch MatchRefresh { get; init; }
        public required ToggleSwitch AutoHdr { get; init; }
        public required ToggleSwitch Thumbs { get; init; }
        public required ToggleSwitch Gamepad { get; init; }
        public required ToggleSwitch Associate { get; init; }
        public required ToggleSwitch ProgressLine { get; init; }
        public required ToggleSwitch Passthrough { get; init; }
        public required ComboBox Quality { get; init; }
        public required ComboBox Language { get; init; }
        public required ComboBox Encoding { get; init; }
        public required ComboBox Ass { get; init; }
    }
}
