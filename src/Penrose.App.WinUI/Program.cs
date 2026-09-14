using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Velopack;

namespace Penrose.App.WinUI;

internal static class Program
{
    private const string InstanceKey = "Penrose";
    private static readonly object RedirectGate = new();
    private static readonly List<AppActivationArguments> PendingRedirects = [];
    private static bool _windowReady;

    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectIfNecessary())
        {
            return;
        }

        Application.Start(_ =>
        {
            DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    internal static void NotifyWindowReady()
    {
        AppActivationArguments[] pending;
        lock (RedirectGate)
        {
            _windowReady = true;
            pending = [.. PendingRedirects];
            PendingRedirects.Clear();
        }

        MainWindow? window = App.CurrentWindow;
        if (window is null)
        {
            return;
        }

        foreach (AppActivationArguments args in pending)
        {
            ApplyActivation(window, args);
        }
    }

    private static bool RedirectIfNecessary()
    {
        AppInstance current = AppInstance.GetCurrent();
        AppActivationArguments args = current.GetActivatedEventArgs();
        // PENROSE_INSTANCE_KEY=<name> runs an independent window (second
        // screen, side-by-side testing) instead of redirecting to the running one.
        string? extra = Environment.GetEnvironmentVariable("PENROSE_INSTANCE_KEY");
        string key = string.IsNullOrWhiteSpace(extra) ? InstanceKey : InstanceKey + ":" + extra.Trim();
        AppInstance main = AppInstance.FindOrRegisterForKey(key);
        if (main.IsCurrent)
        {
            main.Activated += OnRedirected;
            return false;
        }

        Redirect(main, args);
        return true;
    }

    private static void Redirect(AppInstance main, AppActivationArguments args)
    {
        using SemaphoreSlim ready = new(0, 1);
        _ = Task.Run(async () =>
        {
            try
            {
                await main.RedirectActivationToAsync(args);
            }
            finally
            {
                ready.Release();
            }
        });
        ready.Wait();
    }

    private static void OnRedirected(object? sender, AppActivationArguments args)
    {
        lock (RedirectGate)
        {
            if (!_windowReady)
            {
                PendingRedirects.Add(args);
                return;
            }
        }

        MainWindow? window = App.CurrentWindow;
        if (window is null)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(() => ApplyActivation(window, args));
    }

    private static void ApplyActivation(MainWindow window, AppActivationArguments args)
    {
        string? path = ActivationPaths.FromArgs(args);
        if (!string.IsNullOrWhiteSpace(path))
        {
            window.OpenFromShell(path);
        }
        else
        {
            window.BringToFront();
        }
    }
}
