using Penrose.Core.Playback;
using Penrose.Interop.LibMpv;

namespace Penrose.Playback.Mpv.Tests;

/// <summary>
/// Boolean properties used to be observed as MPV_FORMAT_STRING while the mapper
/// only read <c>PropertyFlag</c>, which the real client fills for MPV_FORMAT_FLAG
/// alone. paused-for-cache, seeking, idle-active and vo-configured therefore read
/// false forever: buffering never surfaced and vo-configured pinned Activity to
/// Reconfiguring. These tests pin both the observe format and the mapping.
/// </summary>
public sealed class PropertyObservationTests
{
    [Theory]
    [InlineData("pause")]
    [InlineData("paused-for-cache")]
    [InlineData("idle-active")]
    [InlineData("seeking")]
    [InlineData("vo-configured")]
    public async Task Boolean_properties_are_observed_as_flags(string property)
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();

        Assert.True(harness.Client.Observed.ContainsKey(property), property + " is not observed");
        Assert.Equal(MpvFormat.Flag, harness.Client.Observed[property]);
    }

    [Theory]
    [InlineData("time-pos")]
    [InlineData("duration")]
    public async Task Time_properties_are_observed_as_doubles(string property)
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();

        Assert.Equal(MpvFormat.Double, harness.Client.Observed[property]);
    }

    [Fact]
    public async Task Paused_for_cache_reaches_the_snapshot_and_clears()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.LoadAsync();

        harness.PushProperty("paused-for-cache", flag: true);
        PlaybackSnapshot buffering = await harness.WaitForSnapshotAsync(s => s.PausedForCache);
        Assert.Equal(Activity.Buffering, buffering.Activity);

        harness.PushProperty("paused-for-cache", flag: false);
        PlaybackSnapshot resumed = await harness.WaitForSnapshotAsync(s => !s.PausedForCache);
        Assert.Equal(Activity.None, resumed.Activity);
    }

    [Fact]
    public async Task Vo_configured_clears_the_reconfiguring_activity()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.LoadAsync();

        harness.PushProperty("vo-configured", flag: false);
        await harness.WaitForSnapshotAsync(s => s.Activity == Activity.Reconfiguring);

        harness.PushProperty("vo-configured", flag: true);
        await harness.WaitForSnapshotAsync(s => s.Activity == Activity.None);
    }

    [Fact]
    public async Task Seeking_flag_reaches_the_snapshot_and_clears()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.LoadAsync();

        harness.PushProperty("seeking", flag: true);
        await harness.WaitForSnapshotAsync(s => s.Activity == Activity.Seeking);

        harness.PushProperty("seeking", flag: false);
        await harness.WaitForSnapshotAsync(s => s.Activity == Activity.None);
    }

    [Fact]
    public async Task Idle_active_empties_a_loaded_file()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        PlaybackSnapshot loaded = await harness.LoadAsync();
        Assert.Equal(MediaPhase.Loaded, loaded.MediaPhase);

        harness.PushProperty("idle-active", flag: true);
        await harness.WaitForSnapshotAsync(s => s.MediaPhase == MediaPhase.Empty);
    }

    [Fact]
    public async Task Flag_properties_still_map_when_delivered_as_text()
    {
        // Defence in depth: a property observed as a string carries no flag, and the
        // mapper must fall back to the text rather than silently reading false.
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.LoadAsync();

        harness.PushProperty("paused-for-cache", flag: null, text: "yes");
        PlaybackSnapshot buffering = await harness.WaitForSnapshotAsync(s => s.PausedForCache);
        Assert.Equal(Activity.Buffering, buffering.Activity);

        harness.PushProperty("paused-for-cache", flag: null, text: "no");
        await harness.WaitForSnapshotAsync(s => !s.PausedForCache);
    }

    [Fact]
    public async Task An_unavailable_flag_property_reads_false()
    {
        await using EngineHarness harness = await EngineHarness.StartAsync();
        await harness.LoadAsync();

        harness.PushProperty("paused-for-cache", flag: true);
        await harness.WaitForSnapshotAsync(s => s.PausedForCache);

        // mpv reports an unavailable property with neither flag nor text.
        harness.PushProperty("paused-for-cache", flag: null, text: null);
        await harness.WaitForSnapshotAsync(s => !s.PausedForCache);
    }
}
