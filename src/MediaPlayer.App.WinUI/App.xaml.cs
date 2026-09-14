using MediaPlayer.Diagnostics;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace MediaPlayer.App.WinUI;

public partial class App : Application
{
    public App()
    {
        try
        {
            ConfigureLogging();
            // Player chrome is dark by design; this also themes dialogs and flyouts,
            // which do not inherit RequestedTheme from the window content.
            RequestedTheme = ApplicationTheme.Dark;
            InitializeComponent();
            UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };
        }
        catch (Exception ex)
        {
            // Exceptions here surface only as a XAML 0xc000027b failfast with no
            // managed trace; keep a plain-text record before the process dies.
            WriteCrashFile("App constructor", ex);
            throw;
        }
    }

    public static MainWindow? CurrentWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Log.Information("Launch: creating window");
            CurrentWindow = new MainWindow();
            CurrentWindow.Activate();
            Program.NotifyWindowReady();
            string? path = ActivationPaths.FromArgs(null);
            if (!string.IsNullOrWhiteSpace(path))
            {
                CurrentWindow.OpenFromShell(path);
            }
        }
        catch (Exception ex)
        {
            WriteCrashFile("OnLaunched", ex);
            Log.Fatal(ex, "Launch failed");
            Log.CloseAndFlush();
            throw;
        }
    }

    private static void WriteCrashFile(string stage, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            File.AppendAllText(
                Path.Combine(AppPaths.Logs, "crash.txt"),
                $"{DateTimeOffset.Now:O} {stage}: {ex}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Every <c>async void</c> handler in the window ends up here on failure.
    /// Logging and marking handled keeps a failed property read or a rejected
    /// mpv command from taking the whole player down mid-movie.
    /// </summary>
    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI exception: {Message}", e.Message);
        e.Handled = true;
        Log.CloseAndFlush();
        // Re-open so later events still land in the file.
        ConfigureLogging();
    }

    private static void ConfigureLogging()
    {
        Directory.CreateDirectory(AppPaths.Logs);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                new RedactingTextFormatter(),
                Path.Combine(AppPaths.Logs, "app.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }

    /// <summary>
    /// Redacts at write time so tokens never reach <c>app.log</c>; the diagnostics
    /// export redacts again on top of that.
    /// </summary>
    private sealed class RedactingTextFormatter : ITextFormatter
    {
        private readonly MessageTemplateTextFormatter _inner = new(
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

        public void Format(LogEvent logEvent, TextWriter output)
        {
            using StringWriter buffer = new();
            _inner.Format(logEvent, buffer);
            output.Write(LogRedactor.Redact(buffer.ToString()));
        }
    }
}
