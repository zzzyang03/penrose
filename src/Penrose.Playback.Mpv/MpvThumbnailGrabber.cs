using System.Globalization;
using Penrose.Interop.LibMpv;

namespace Penrose.Playback.Mpv;

/// <summary>
/// Second libmpv handle for seek-bar stills. Must not touch the playback VO.
/// Uses <c>vo=null</c> (no window at all: an "off-screen" geometry is clamped back
/// onto the desktop by mpv's win32 backend), software decode so it never competes
/// with the main player for a hardware decoder, and a small <c>vf=scale</c> so
/// each still is thumbnail-sized instead of a full-resolution frame.
/// </summary>
public sealed class MpvThumbnailGrabber : IAsyncDisposable
{
    /// <summary>Width of the decoded still; height follows the aspect ratio.</summary>
    public const int ThumbnailWidth = 320;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;
    private readonly Func<IMpvClient> _clientFactory;
    private IMpvClient? _client;
    private string? _loaded;
    private bool _disposed;

    public MpvThumbnailGrabber(string directory, Func<IMpvClient>? clientFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _clientFactory = clientFactory ?? NativeMpvClient.Create;
        Directory.CreateDirectory(_directory);
        CleanDirectory(_directory);
    }

    /// <summary>Delete stills left behind by a previous run or crash.</summary>
    public static void CleanDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.jpg"))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // In use by an Image control; the next run gets it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Every step below blocks: creating the second mpv instance, the synchronous
    /// commands, and <c>WaitEventAsync</c>, which wraps a blocking
    /// <c>mpv_wait_event</c> in an already-completed task. None of it yields, so
    /// the whole capture is pushed onto the pool rather than left to run inline on
    /// the caller — on the UI thread that was a freeze of up to eight seconds.
    /// </summary>
    public Task<string?> CaptureAsync(
        string url,
        TimeSpan position,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return Task.Run(
            () => CaptureCoreAsync(url, position, outputPath, cancellationToken),
            cancellationToken);
    }

    private async Task<string?> CaptureCoreAsync(
        string url,
        TimeSpan position,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return null;
            }

            IMpvClient client = EnsureClient();
            if (!string.Equals(_loaded, url, StringComparison.OrdinalIgnoreCase))
            {
                _loaded = null;
                await DrainAsync(client, cancellationToken).ConfigureAwait(false);
                client.Command(["loadfile", url, "replace"]);
                if (!await WaitForAsync(client, MpvEventId.FileLoaded, TimeSpan.FromSeconds(8), cancellationToken)
                    .ConfigureAwait(false))
                {
                    return null;
                }

                _loaded = url;
            }

            client.SetProperty("pause", "yes");
            // FILE_LOADED leaves the first PLAYBACK_RESTART queued; consuming it as
            // the seek's completion would screenshot the pre-seek frame.
            await DrainAsync(client, cancellationToken).ConfigureAwait(false);
            client.Command(
            [
                "seek",
                Math.Max(0, position.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture),
                "absolute+exact",
            ]);
            if (!await WaitForAsync(client, MpvEventId.PlaybackRestart, TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false))
            {
                return null;
            }

            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            client.Command(["screenshot-to-file", outputPath, "video"]);
            return File.Exists(outputPath) ? outputPath : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is MpvException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // A failed still is cosmetic; the next hover retries. A broken core is
            // dropped so EnsureClient can rebuild it.
            if (ex is MpvException)
            {
                _loaded = null;
                await ResetClientAsync().ConfigureAwait(false);
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // A capture stuck inside libmpv must not hold the window close hostage.
        bool acquired = await _gate.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        try
        {
            await ResetClientAsync().ConfigureAwait(false);
            CleanDirectory(_directory);
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }

            _gate.Dispose();
        }
    }

    private async Task ResetClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        IMpvClient client = _client;
        _client = null;
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (MpvException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private IMpvClient EnsureClient()
    {
        if (_client is not null)
        {
            return _client;
        }

        IMpvClient client = _clientFactory();
        try
        {
            client.SetProperty("config", "no");
            client.SetProperty("terminal", "no");
            client.SetProperty("load-scripts", "no");
            client.SetProperty("ytdl", "no");
            client.SetProperty("vo", "null");
            client.SetProperty("force-window", "no");
            client.SetProperty("hwdec", "no");
            client.SetProperty("osc", "no");
            client.SetProperty("osd-bar", "no");
            client.SetProperty("idle", "yes");
            client.SetProperty("pause", "yes");
            client.SetProperty("aid", "no");
            client.SetProperty("sid", "no");
            client.SetProperty("hr-seek", "yes");
            client.SetProperty("input-default-bindings", "no");
            client.SetProperty("input-vo-keyboard", "no");
            client.SetProperty("keep-open", "yes");
            client.SetProperty("vf", "scale=w=" + ThumbnailWidth.ToString(CultureInfo.InvariantCulture) + ":h=-2");
            client.SetProperty("screenshot-format", "jpg");
            client.SetProperty("screenshot-jpeg-quality", "75");
            client.SetProperty("screenshot-high-bit-depth", "no");
            client.SetProperty("screenshot-tag-colorspace", "no");
            client.Initialize();
        }
        catch
        {
            // mpv_create succeeded but setup failed: tear the core down now rather
            // than letting the SafeHandle finalizer do it on the finalizer thread.
            client.TerminateDestroyAsync().GetAwaiter().GetResult();
            throw;
        }

        _client = client;
        return client;
    }

    private static async Task DrainAsync(IMpvClient client, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 256; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MpvClientEvent? evt = await client.WaitEventAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            if (evt is null)
            {
                return;
            }
        }
    }

    private static async Task<bool> WaitForAsync(
        IMpvClient client,
        MpvEventId id,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remain = deadline - DateTime.UtcNow;
            if (remain <= TimeSpan.Zero)
            {
                break;
            }

            MpvClientEvent? evt = await client.WaitEventAsync(
                    remain > TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : remain,
                    cancellationToken)
                .ConfigureAwait(false);
            if (evt is null)
            {
                continue;
            }

            if (evt.Id == id)
            {
                return evt.Error >= 0;
            }

            if (evt.Id == MpvEventId.EndFile && evt.EndFileReason == MpvEndFileReason.Error)
            {
                return false;
            }
        }

        return false;
    }
}
