using MediaPlayer.Persistence;

namespace MediaPlayer.Persistence.Tests;

public sealed class PlaybackStoreTests
{
    [Fact]
    public async Task Upsert_and_read_progress()
    {
        string path = Path.Combine(Path.GetTempPath(), "mediaplayer-tests", Guid.NewGuid().ToString("N") + ".db");
        await using PlaybackStore store = new(path);
        await store.InitializeAsync();
        PlaybackProgressRecord record = new("file:///tmp/a.mkv", 12_000, 90_000, DateTimeOffset.Parse("2026-09-03T00:00:00Z"));
        await store.UpsertProgressAsync(record);

        PlaybackProgressRecord? loaded = await store.GetProgressAsync(record.Uri);
        Assert.NotNull(loaded);
        Assert.Equal(12_000, loaded.PositionMs);
        Assert.Equal(90_000, loaded.DurationMs);
    }

    [Fact]
    public async Task Settings_roundtrip()
    {
        string path = Path.Combine(Path.GetTempPath(), "mediaplayer-tests", Guid.NewGuid().ToString("N") + ".db");
        await using PlaybackStore store = new(path);
        await store.InitializeAsync();
        Assert.Null(await store.GetSettingAsync("simple"));
        await store.SetSettingAsync("simple", "{\"volume\":40}");
        Assert.Equal("{\"volume\":40}", await store.GetSettingAsync("simple"));
        await store.SetSettingAsync("simple", "{\"volume\":80}");
        Assert.Equal("{\"volume\":80}", await store.GetSettingAsync("simple"));
    }

    [Fact]
    public async Task Concurrent_commands_serialize_on_one_connection()
    {
        string path = Path.Combine(Path.GetTempPath(), "mediaplayer-tests", Guid.NewGuid().ToString("N") + ".db");
        await using PlaybackStore store = new(path);
        await store.InitializeAsync();
        await Task.WhenAll(Enumerable.Range(0, 32).Select(async i =>
        {
            string uri = "file:///tmp/concurrent-" + i + ".mkv";
            await store.UpsertProgressAsync(new PlaybackProgressRecord(
                uri,
                i * 1000,
                90_000,
                DateTimeOffset.Parse("2026-09-03T00:00:00Z")));
            PlaybackProgressRecord? loaded = await store.GetProgressAsync(uri);
            Assert.NotNull(loaded);
            Assert.Equal(i * 1000, loaded.PositionMs);
            string key = "k" + i;
            await store.SetSettingAsync(key, "v" + i);
            Assert.Equal("v" + i, await store.GetSettingAsync(key));
        }));
    }
}
