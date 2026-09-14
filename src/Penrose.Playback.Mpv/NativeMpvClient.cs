using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Penrose.Interop.LibMpv;

namespace Penrose.Playback.Mpv;

/// <summary>
/// Real libmpv client. <see cref="MpvHandle"/> is the only path that calls
/// <c>mpv_terminate_destroy</c>.
/// </summary>
public sealed class NativeMpvClient : IMpvClient
{
    private readonly object _sync = new();

    /// <summary>Serializes <c>mpv_wait_event</c> only; never held by any other call.</summary>
    private readonly object _waitSync = new();

    private MpvHandle? _handle;
    private ulong _reply;
    private bool _initialized;

    private NativeMpvClient(MpvHandle handle)
    {
        _handle = handle;
    }

    public bool TerminateDestroyCalled { get; private set; }

    public static NativeMpvClient Create()
    {
        if (!MpvNativeLibrary.TryLoad(out _, out string? error))
        {
            throw new InvalidOperationException(error);
        }

        nint ctx = NativeMethods.mpv_create();
        if (ctx == 0)
        {
            throw new MpvException(MpvError.NoMemory, "mpv_create returned null");
        }

        return new NativeMpvClient(new MpvHandle(ctx));
    }

    public void Initialize()
    {
        lock (_sync)
        {
            MpvException.ThrowIfError(NativeMethods.mpv_initialize(RawHandle()), "mpv_initialize");
            _initialized = true;
        }
    }

    public ulong NextReplyUserdata()
    {
        lock (_sync)
        {
            return ++_reply;
        }
    }

    public void Command(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
        {
            throw new ArgumentException("mpv command requires at least one argument.", nameof(args));
        }

        nint[] pointers = new nint[args.Count + 1];
        GCHandle[] pins = new GCHandle[args.Count];
        GCHandle arrayPin = default;
        try
        {
            for (int i = 0; i < args.Count; i++)
            {
                byte[] utf8 = MpvNode.Utf8Z(args[i]);
                pins[i] = GCHandle.Alloc(utf8, GCHandleType.Pinned);
                pointers[i] = pins[i].AddrOfPinnedObject();
            }

            arrayPin = GCHandle.Alloc(pointers, GCHandleType.Pinned);
            lock (_sync)
            {
                MpvException.ThrowIfError(
                    NativeMethods.mpv_command(RawHandle(), arrayPin.AddrOfPinnedObject()),
                    "mpv_command " + args[0]);
            }
        }
        finally
        {
            if (arrayPin.IsAllocated)
            {
                arrayPin.Free();
            }

            foreach (GCHandle pin in pins)
            {
                if (pin.IsAllocated)
                {
                    pin.Free();
                }
            }
        }
    }

    public void CommandAsync(IReadOnlyList<string> args, ulong replyUserdata)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
        {
            throw new ArgumentException("mpv command requires at least one argument.", nameof(args));
        }

        nint[] pointers = new nint[args.Count + 1];
        GCHandle[] pins = new GCHandle[args.Count];
        GCHandle arrayPin = default;
        try
        {
            for (int i = 0; i < args.Count; i++)
            {
                byte[] utf8 = MpvNode.Utf8Z(args[i]);
                pins[i] = GCHandle.Alloc(utf8, GCHandleType.Pinned);
                pointers[i] = pins[i].AddrOfPinnedObject();
            }

            arrayPin = GCHandle.Alloc(pointers, GCHandleType.Pinned);
            lock (_sync)
            {
                MpvException.ThrowIfError(
                    NativeMethods.mpv_command_async(RawHandle(), replyUserdata, arrayPin.AddrOfPinnedObject()),
                    "mpv_command_async " + args[0]);
            }
        }
        finally
        {
            if (arrayPin.IsAllocated)
            {
                arrayPin.Free();
            }

            foreach (GCHandle pin in pins)
            {
                if (pin.IsAllocated)
                {
                    pin.Free();
                }
            }
        }
    }

    public void SetProperty(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        byte[] nameUtf8 = MpvNode.Utf8Z(name);
        byte[] valueUtf8 = MpvNode.Utf8Z(value);
        lock (_sync)
        {
            nint ctx = RawHandle();
            int code = _initialized
                ? NativeMethods.mpv_set_property_string(ctx, nameUtf8, valueUtf8)
                : NativeMethods.mpv_set_option_string(ctx, nameUtf8, valueUtf8);
            MpvException.ThrowIfError(code, _initialized ? "mpv_set_property_string" : "mpv_set_option_string");
        }
    }

    public string? GetPropertyString(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_sync)
        {
            nint ptr = NativeMethods.mpv_get_property_string(RawHandle(), MpvNode.Utf8Z(name));
            try
            {
                return MpvNode.PtrToStringUtf8(ptr);
            }
            finally
            {
                MpvNode.Free(ptr);
            }
        }
    }

    public long? GetPropertyInt64(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        nint buffer = Marshal.AllocHGlobal(sizeof(long));
        try
        {
            Marshal.WriteInt64(buffer, 0);
            lock (_sync)
            {
                int code = NativeMethods.mpv_get_property(
                    RawHandle(),
                    MpvNode.Utf8Z(name),
                    (int)MpvFormat.Int64,
                    buffer);
                if (code < 0)
                {
                    return null;
                }
            }

            return Marshal.ReadInt64(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void RequestLogMessages(string minLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minLevel);
        lock (_sync)
        {
            MpvException.ThrowIfError(
                NativeMethods.mpv_request_log_messages(RawHandle(), MpvNode.Utf8Z(minLevel)),
                "mpv_request_log_messages");
        }
    }

    public void ObserveProperty(string name, MpvFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_sync)
        {
            MpvException.ThrowIfError(
                NativeMethods.mpv_observe_property(
                    RawHandle(),
                    userdata: 0,
                    MpvNode.Utf8Z(name),
                    (int)format),
                "mpv_observe_property");
        }
    }

    public Task<MpvClientEvent?> WaitEventAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenRegistration registration = cancellationToken.Register(Wakeup);
        MpvClientEvent? evt = WaitEvent(timeout);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(evt);
    }

    public Task TerminateDestroyAsync()
    {
        lock (_sync)
        {
            if (_handle is { IsInvalid: false } handle)
            {
                handle.Dispose();
                _handle = null;
                TerminateDestroyCalled = true;
            }
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(TerminateDestroyAsync());

    private void Wakeup()
    {
        if (!TryRentHandle(out MpvHandle? handle))
        {
            return;
        }

        try
        {
            NativeMethods.mpv_wakeup(handle.DangerousGetHandle());
        }
        finally
        {
            handle.DangerousRelease();
        }
    }

    /// <summary>
    /// Ref-counted lease on the mpv context. Lets a blocking call run outside
    /// <c>_sync</c> while still keeping a concurrent
    /// <see cref="TerminateDestroyAsync"/> from freeing the context underneath it:
    /// <see cref="MpvHandle"/> defers <c>mpv_terminate_destroy</c> until the last
    /// lease is released. Callers must always <c>DangerousRelease</c>.
    /// </summary>
    private bool TryRentHandle([NotNullWhen(true)] out MpvHandle? handle)
    {
        lock (_sync)
        {
            handle = _handle is { IsInvalid: false } live ? live : null;
        }

        if (handle is null)
        {
            return false;
        }

        bool rented = false;
        try
        {
            handle.DangerousAddRef(ref rented);
        }
        catch (ObjectDisposedException)
        {
            rented = false;
        }

        if (!rented)
        {
            handle = null;
        }

        return rented;
    }

    private MpvClientEvent? WaitEvent(TimeSpan timeout)
    {
        // mpv_wait_event blocks for up to `timeout`. Holding _sync across it would
        // stall every other call on this client — including Wakeup, whose only job
        // is to interrupt this very wait, which made cancellation a no-op.
        // The handle lease keeps the context alive instead; _waitSync only preserves
        // libmpv's rule that one thread at a time may wait for events.
        if (!TryRentHandle(out MpvHandle? handle))
        {
            throw new ObjectDisposedException(nameof(NativeMpvClient));
        }

        // The returned mpv_event is only valid until the next mpv_wait_event on this
        // handle or until the handle is destroyed, so it must be copied into managed
        // memory while the lease and the wait lock are still held.
        try
        {
            lock (_waitSync)
            {
                nint ptr = NativeMethods.mpv_wait_event(handle.DangerousGetHandle(), timeout.TotalSeconds);
                return ptr == 0 ? null : ReadEvent(ptr);
            }
        }
        finally
        {
            handle.DangerousRelease();
        }
    }

    private static MpvClientEvent? ReadEvent(nint ptr)
    {
        MpvEventNative native = Marshal.PtrToStructure<MpvEventNative>(ptr);
        MpvEventId id = (MpvEventId)native.EventId;
        if (id == MpvEventId.None)
        {
            return null;
        }

        MpvEndFileReason? endFileReason = null;
        string? errorString = native.Error < 0
            ? MpvNode.PtrToStringUtf8(NativeMethods.mpv_error_string(native.Error))
            : null;
        string? propertyName = null;
        string? propertyString = null;
        bool? propertyFlag = null;

        if (id == MpvEventId.LogMessage && native.Data != 0)
        {
            MpvEventLogMessageNative log = Marshal.PtrToStructure<MpvEventLogMessageNative>(native.Data);
            return new MpvClientEvent(
                id,
                native.ReplyUserdata,
                native.Error,
                LogPrefix: MpvNode.PtrToStringUtf8(log.Prefix),
                LogLevel: MpvNode.PtrToStringUtf8(log.Level),
                LogText: MpvNode.PtrToStringUtf8(log.Text)?.TrimEnd('\n', '\r'));
        }

        if (id == MpvEventId.EndFile && native.Data != 0)
        {
            MpvEventEndFileNative end = Marshal.PtrToStructure<MpvEventEndFileNative>(native.Data);
            endFileReason = (MpvEndFileReason)end.Reason;
            if (end.Error < 0)
            {
                errorString = MpvNode.PtrToStringUtf8(NativeMethods.mpv_error_string(end.Error));
            }
        }
        else if (id == MpvEventId.PropertyChange && native.Data != 0)
        {
            MpvEventPropertyNative property = Marshal.PtrToStructure<MpvEventPropertyNative>(native.Data);
            propertyName = MpvNode.PtrToStringUtf8(property.Name);
            (propertyString, propertyFlag) = ReadPropertyData(property);
        }

        return new MpvClientEvent(
            id,
            native.ReplyUserdata,
            native.Error,
            endFileReason,
            errorString,
            propertyName,
            propertyString,
            propertyFlag);
    }

    private static (string? Text, bool? Flag) ReadPropertyData(MpvEventPropertyNative property)
    {
        if (property.Data == 0)
        {
            return (null, null);
        }

        switch ((MpvFormat)property.Format)
        {
            case MpvFormat.String:
            case MpvFormat.OsdString:
                nint strPtr = Marshal.ReadIntPtr(property.Data);
                return (MpvNode.PtrToStringUtf8(strPtr), null);
            case MpvFormat.Flag:
                bool flag = Marshal.ReadInt32(property.Data) != 0;
                return (flag ? "yes" : "no", flag);
            case MpvFormat.Double:
                double value = Marshal.PtrToStructure<double>(property.Data);
                return (value.ToString("G17", CultureInfo.InvariantCulture), null);
            case MpvFormat.Int64:
                long integer = Marshal.ReadInt64(property.Data);
                return (integer.ToString(CultureInfo.InvariantCulture), null);
            default:
                return (null, null);
        }
    }

    private nint RawHandle()
    {
        if (_handle is null || _handle.IsInvalid)
        {
            throw new ObjectDisposedException(nameof(NativeMpvClient));
        }

        return _handle.DangerousGetHandle();
    }
}
