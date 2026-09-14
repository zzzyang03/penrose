using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace Penrose.App.WinUI;

/// <summary>
/// Minimal <see cref="ILogger"/> over the static Serilog logger for library code
/// (the mpv engine) that must not depend on Serilog directly.
/// </summary>
internal sealed class SerilogLoggerAdapter : ILogger
{
    private readonly string _source;

    public SerilogLoggerAdapter(string source)
    {
        _source = source;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && Serilog.Log.IsEnabled(Map(logLevel));

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel))
        {
            return;
        }

        Serilog.Log.Write(Map(logLevel), exception, "[{Source}] {Message}", _source, formatter(state, exception));
    }

    private static LogEventLevel Map(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            _ => LogEventLevel.Fatal,
        };
}
