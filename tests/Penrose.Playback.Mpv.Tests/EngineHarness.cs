using Penrose.Core.Options;
using Penrose.Core.Playback;
using Penrose.Interop.LibMpv;

namespace Penrose.Playback.Mpv.Tests;

/// <summary>
/// An engine wired to <see cref="FakeMpvClient"/>, plus the two things every test
/// needs: getting a file to Loaded (so file-scoped events are not dropped by the
/// generation filter) and waiting for the snapshot to settle.
/// </summary>
internal sealed class EngineHarness : IAsyncDisposable
{
    private EngineHarness(MpvPlaybackEngine engine, FakeMpvClient client)
    {
        Engine = engine;
        Client = client;
    }

    public MpvPlaybackEngine Engine { get; }

    public FakeMpvClient Client { get; }

    public static async Task<EngineHarness> StartAsync(
        SurfaceBootstrapOptions? surface = null,
        PlaybackPolicyOptions? policy = null)
    {
        FakeMpvClient client = new();
        MpvPlaybackEngine engine = new(client, engineOptions: null, surface, policy);
        await engine.InitializeAsync();
        return new EngineHarness(engine, client);
    }

    public async Task<PlaybackSnapshot> LoadAsync(string uri = "https://example.invalid/a.mkv")
    {
        Task<PlaybackSnapshot> load = Engine.LoadAsync(new PlaybackRequest
        {
            RequestId = Guid.NewGuid(),
            Uri = new Uri(uri),
        });
        await Client.LoadfileIssued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Client.Push(new MpvClientEvent(MpvEventId.StartFile, 1, 0));
        Client.Push(new MpvClientEvent(MpvEventId.FileLoaded, 1, 0));
        return await load.WaitAsync(TimeSpan.FromSeconds(2));
    }

    public void PushProperty(string name, bool? flag = null, string? text = null) =>
        Client.Push(new MpvClientEvent(
            MpvEventId.PropertyChange,
            ReplyUserdata: 0,
            Error: 0,
            PropertyName: name,
            PropertyString: text,
            PropertyFlag: flag));

    public async Task<PlaybackSnapshot> WaitForSnapshotAsync(Func<PlaybackSnapshot, bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            PlaybackSnapshot snapshot = Engine.Snapshot;
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Snapshot never matched. Last seen: " + Engine.Snapshot);
    }

    public async ValueTask DisposeAsync() => await Engine.DisposeAsync();
}
