using MediaPlayer.Core.Playback;

namespace MediaPlayer.Core.Tests;

public sealed class PlaybackReducerTests
{
    private readonly PlaybackReducer _reducer = new();

    [Fact]
    public void Created_snapshot_is_empty_with_null_position()
    {
        PlaybackSnapshot snapshot = PlaybackSnapshot.Created;

        Assert.Equal(EngineLifecycle.Created, snapshot.EngineLifecycle);
        Assert.Equal(MediaPhase.Empty, snapshot.MediaPhase);
        Assert.Equal(Activity.None, snapshot.Activity);
        Assert.Null(snapshot.Position);
        Assert.Empty(snapshot.InvariantViolations());
    }

    [Fact]
    public void Empty_phase_forces_activity_none_and_null_position()
    {
        PlaybackSnapshot loaded = Reduce(
            PlaybackSnapshot.Created,
            Init(),
            Load(1),
            FileLoaded(1),
            Pos(1, TimeSpan.FromSeconds(12)));

        PlaybackSnapshot stopped = Reduce(loaded, End(1, EndFileReason.Stop));

        Assert.Equal(MediaPhase.Empty, stopped.MediaPhase);
        Assert.Equal(Activity.None, stopped.Activity);
        Assert.Null(stopped.Position);
        Assert.Empty(stopped.InvariantViolations());
    }

    [Fact]
    public void Old_generation_events_do_not_mutate_snapshot()
    {
        PlaybackSnapshot current = Reduce(
            PlaybackSnapshot.Created,
            Init(),
            Load(2),
            FileLoaded(2),
            Pos(2, TimeSpan.FromSeconds(5)));

        PlaybackSnapshot afterStale = Reduce(
            current,
            new PositionChangedEvent { Generation = 1, Position = TimeSpan.FromSeconds(99) },
            new EndFileEvent { Generation = 1, Reason = EndFileReason.Error, Error = "stale" },
            new PausedForCacheEvent { Generation = 1, PausedForCache = true, BufferingPercent = 10 });

        Assert.Equal(current, afterStale);
        Assert.Equal(2, afterStale.PlaybackGeneration);
        Assert.Equal(TimeSpan.FromSeconds(5), afterStale.Position);
        Assert.Null(afterStale.Error);
    }

    [Fact]
    public void Disposing_freezes_subsequent_file_events()
    {
        PlaybackSnapshot live = Reduce(
            PlaybackSnapshot.Created,
            Init(),
            Load(1),
            FileLoaded(1));

        PlaybackSnapshot disposing = Reduce(
            live,
            new EngineDisposingEvent { Generation = 1 });

        PlaybackSnapshot after = Reduce(
            disposing,
            new EndFileEvent { Generation = 1, Reason = EndFileReason.Error, Error = "late" },
            new PositionChangedEvent { Generation = 1, Position = TimeSpan.FromSeconds(3) });

        Assert.Equal(EngineLifecycle.Disposing, after.EngineLifecycle);
        Assert.Equal(disposing, after);
    }

    [Fact]
    public void Disposed_is_also_frozen()
    {
        PlaybackSnapshot disposed = Reduce(
            PlaybackSnapshot.Created,
            Init(),
            new EngineDisposingEvent { Generation = 0 },
            new EngineDisposedEvent { Generation = 0 });

        PlaybackSnapshot after = Reduce(
            disposed,
            new EngineFaultedEvent { Generation = 0, Error = "too late" });

        Assert.Equal(EngineLifecycle.Disposed, after.EngineLifecycle);
        Assert.Equal(disposed, after);
    }

    [Fact]
    public void Buffering_requires_paused_for_cache()
    {
        PlaybackSnapshot loaded = Reduce(
            PlaybackSnapshot.Created,
            Init(),
            Load(1),
            FileLoaded(1));

        PlaybackSnapshot buffering = Reduce(
            loaded,
            new PausedForCacheEvent { Generation = 1, PausedForCache = true, BufferingPercent = 40 });

        Assert.Equal(Activity.Buffering, buffering.Activity);
        Assert.True(buffering.PausedForCache);
        Assert.Equal(40, buffering.BufferingPercent);
        Assert.Empty(buffering.InvariantViolations());

        PlaybackSnapshot resumed = Reduce(
            buffering,
            new PausedForCacheEvent { Generation = 1, PausedForCache = false });

        Assert.Equal(Activity.None, resumed.Activity);
        Assert.False(resumed.PausedForCache);
        Assert.Null(resumed.BufferingPercent);
    }

    [Fact]
    public void StartFile_maps_to_opening_and_FileLoaded_to_loaded()
    {
        PlaybackSnapshot opening = Reduce(PlaybackSnapshot.Created, Init(), Load(1), new StartFileEvent { Generation = 1 });
        Assert.Equal(MediaPhase.Opening, opening.MediaPhase);

        PlaybackSnapshot loaded = Reduce(opening, FileLoaded(1));
        Assert.Equal(MediaPhase.Loaded, loaded.MediaPhase);
    }

    [Fact]
    public void EndFile_error_maps_to_failed()
    {
        PlaybackSnapshot failed = Reduce(
            PlaybackSnapshot.Created,
            Init(),
            Load(1),
            FileLoaded(1),
            End(1, EndFileReason.Error, "demux error"));

        Assert.Equal(MediaPhase.Failed, failed.MediaPhase);
        Assert.Equal("demux error", failed.Error);
        Assert.Equal(Activity.None, failed.Activity);
    }

    [Fact]
    public void Idle_during_opening_does_not_clear_generation()
    {
        PlaybackSnapshot opening = Reduce(PlaybackSnapshot.Created, Init(), Load(4));
        PlaybackSnapshot stillOpening = Reduce(opening, new IdleActiveEvent { Generation = 4, Idle = true });

        Assert.Equal(MediaPhase.Opening, stillOpening.MediaPhase);
        Assert.Equal(4, stillOpening.PlaybackGeneration);
    }

    [Fact]
    public void Idle_after_loaded_clears_media()
    {
        PlaybackSnapshot loaded = Reduce(PlaybackSnapshot.Created, Init(), Load(1), FileLoaded(1), Pos(1, TimeSpan.FromSeconds(1)));
        PlaybackSnapshot empty = Reduce(loaded, new IdleActiveEvent { Generation = 1, Idle = true });

        Assert.Equal(MediaPhase.Empty, empty.MediaPhase);
        Assert.Null(empty.Position);
    }

    [Fact]
    public void FileLoaded_defaults_intent_to_playing()
    {
        PlaybackSnapshot loaded = Reduce(
            PlaybackSnapshot.Created,
            Init(),
            Load(1),
            FileLoaded(1));

        Assert.Equal(PlaybackIntent.Playing, loaded.PlaybackIntent);
        Assert.Equal(MediaPhase.Loaded, loaded.MediaPhase);
    }

    [Fact]
    public void Pause_maps_to_intent_not_phase()
    {
        PlaybackSnapshot playing = Reduce(
            PlaybackSnapshot.Created,
            Init(),
            Load(1),
            FileLoaded(1),
            new PauseChangedEvent { Generation = 1, Paused = false });
        PlaybackSnapshot paused = Reduce(playing, new PauseChangedEvent { Generation = 1, Paused = true });

        Assert.Equal(PlaybackIntent.Playing, playing.PlaybackIntent);
        Assert.Equal(PlaybackIntent.Paused, paused.PlaybackIntent);
        Assert.Equal(MediaPhase.Loaded, paused.MediaPhase);
    }

    [Fact]
    public void Engine_fault_is_generation_agnostic()
    {
        PlaybackSnapshot live = Reduce(PlaybackSnapshot.Created, Init(), Load(3), FileLoaded(3));
        PlaybackSnapshot faulted = Reduce(live, new EngineFaultedEvent { Generation = 0, Error = "device lost" });

        Assert.Equal(EngineLifecycle.Faulted, faulted.EngineLifecycle);
        Assert.Equal("device lost", faulted.Error);
    }

    private PlaybackSnapshot Reduce(PlaybackSnapshot start, params PlaybackEvent[] events)
    {
        PlaybackSnapshot current = start;
        foreach (PlaybackEvent evt in events)
        {
            current = _reducer.Reduce(current, evt);
        }

        return current;
    }

    private static EngineInitializedEvent Init() => new() { Generation = 0 };

    private static BeginLoadEvent Load(long generation) => new() { Generation = generation };

    private static FileLoadedEvent FileLoaded(long generation) => new() { Generation = generation };

    private static PositionChangedEvent Pos(long generation, TimeSpan position) =>
        new() { Generation = generation, Position = position };

    private static EndFileEvent End(long generation, EndFileReason reason, string? error = null) =>
        new() { Generation = generation, Reason = reason, Error = error };
}
