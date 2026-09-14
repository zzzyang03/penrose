using System.Threading.Channels;
using MediaPlayer.Interop.LibMpv;
using MediaPlayer.Playback.Mpv;

namespace MediaPlayer.Playback.Mpv.Tests;

internal sealed class FakeMpvClient : IMpvClient
{
    private readonly Channel<MpvClientEvent> _events = Channel.CreateUnbounded<MpvClientEvent>();
    private ulong _reply;

    public List<IReadOnlyList<string>> Commands { get; } = [];

    public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>Format each property was observed under, so tests can pin it down.</summary>
    public Dictionary<string, MpvFormat> Observed { get; } = new(StringComparer.Ordinal);

    /// <summary>Set to stall the engine command loop inside a command.</summary>
    public ManualResetEventSlim? CommandBlock { get; set; }

    public TaskCompletionSource LoadfileIssued { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource StopIssued { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Thrown once by the next <see cref="CommandAsync"/>, like a synchronous mpv rejection.</summary>
    public Exception? FailNextCommand { get; set; }

    public bool TerminateDestroyCalled { get; private set; }

    public void Initialize()
    {
    }

    public ulong NextReplyUserdata() => ++_reply;

    public void Command(IReadOnlyList<string> args) => CommandAsync(args, NextReplyUserdata());

    public void CommandAsync(IReadOnlyList<string> args, ulong replyUserdata)
    {
        CommandBlock?.Wait();
        if (FailNextCommand is { } failure)
        {
            FailNextCommand = null;
            throw failure;
        }

        Commands.Add(args);
        if (args.Count > 0 && args[0] == "loadfile")
        {
            LoadfileIssued.TrySetResult();
        }

        if (args.Count > 0 && args[0] == "stop")
        {
            StopIssued.TrySetResult();
        }
    }

    public void SetProperty(string name, string value) => Properties[name] = value;

    public string? GetPropertyString(string name) =>
        Properties.TryGetValue(name, out string? value) ? value : null;

    public long? GetPropertyInt64(string name) =>
        Properties.TryGetValue(name, out string? value) &&
        long.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;

    public void ObserveProperty(string name, MpvFormat format) => Observed[name] = format;

    public string? LogLevelRequested { get; private set; }

    public void RequestLogMessages(string minLevel) => LogLevelRequested = minLevel;

    public void Push(MpvClientEvent evt) => _events.Writer.TryWrite(evt);

    public async Task<MpvClientEvent?> WaitEventAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            return await _events.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    public Task TerminateDestroyAsync()
    {
        TerminateDestroyCalled = true;
        _events.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(TerminateDestroyAsync());
}
