using System.Diagnostics;
using Penrose.Interop.LibMpv;

namespace Penrose.Playback.Mpv.Tests;

public sealed class NativeMpvClientTests
{
    /// <summary>
    /// mpv_wait_event used to run while holding the client lock, so mpv_wakeup —
    /// the only thing that can cut a wait short — blocked on that same lock and
    /// cancellation degraded into waiting out the full timeout.
    /// </summary>
    [WindowsFact]
    public async Task Cancelling_a_wait_interrupts_it()
    {
        await using NativeMpvClient client = Idle();
        await DrainAsync(client);

        using CancellationTokenSource cts = new();
        Task wait = Task.Run(async () =>
        {
            try
            {
                await client.WaitEventAsync(TimeSpan.FromSeconds(30), cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        });

        await Task.Delay(200);
        Stopwatch elapsed = Stopwatch.StartNew();
        await cts.CancelAsync();
        await wait.WaitAsync(TimeSpan.FromSeconds(10));
        elapsed.Stop();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"cancel took {elapsed.Elapsed}");
    }

    /// <summary>A wait in flight must not stall unrelated calls on the same client.</summary>
    [WindowsFact]
    public async Task A_wait_in_flight_does_not_stall_other_calls()
    {
        await using NativeMpvClient client = Idle();
        await DrainAsync(client);

        using CancellationTokenSource cts = new();
        Task wait = Task.Run(async () =>
        {
            try
            {
                await client.WaitEventAsync(TimeSpan.FromSeconds(20), cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        });

        await Task.Delay(200);
        Stopwatch elapsed = Stopwatch.StartNew();
        string? version = client.GetPropertyString("mpv-version");
        elapsed.Stop();

        await cts.CancelAsync();
        await wait.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"read waited {elapsed.Elapsed}");
    }

    private static NativeMpvClient Idle()
    {
        NativeMpvClient client = NativeMpvClient.Create();
        client.SetProperty("vo", "null");
        client.SetProperty("ao", "null");
        client.SetProperty("osc", "no");
        client.SetProperty("osd-bar", "no");
        client.SetProperty("terminal", "no");
        client.SetProperty("idle", "yes");
        client.SetProperty("force-window", "no");
        client.Initialize();
        return client;
    }

    /// <summary>Empties the startup events so the next wait really blocks.</summary>
    private static async Task DrainAsync(NativeMpvClient client)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (await client.WaitEventAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None) is null)
            {
                return;
            }
        }
    }

    [WindowsFact]
    public async Task Create_initialize_and_read_version()
    {
        await using NativeMpvClient client = NativeMpvClient.Create();
        client.SetProperty("vo", "null");
        client.SetProperty("ao", "null");
        client.SetProperty("osc", "no");
        client.SetProperty("osd-bar", "no");
        client.SetProperty("terminal", "no");
        client.SetProperty("idle", "yes");
        client.SetProperty("force-window", "no");
        client.Initialize();

        string? version = client.GetPropertyString("mpv-version");
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains("mpv", version, StringComparison.OrdinalIgnoreCase);

        string? ffmpeg = client.GetPropertyString("ffmpeg-version");
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg));

        Assert.True(client.GetPropertyInt64("playlist-count") is >= 0);

        await client.TerminateDestroyAsync();
        Assert.True(client.TerminateDestroyCalled);
        Assert.True(MpvNativeLibrary.TryLoad(out _, out _));
    }
}
