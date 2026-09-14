namespace Penrose.Core.Playback;

/// <summary>
/// Pure function from snapshot + event to next snapshot.
/// Enforces v3 invariants; never mutates the input record.
/// </summary>
public sealed class PlaybackReducer
{
    public PlaybackSnapshot Reduce(PlaybackSnapshot current, PlaybackEvent evt)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(evt);

        if (current.EngineLifecycle == EngineLifecycle.Disposed)
        {
            return current;
        }

        if (current.EngineLifecycle == EngineLifecycle.Disposing)
        {
            return evt is EngineDisposedEvent
                ? current with { EngineLifecycle = EngineLifecycle.Disposed }
                : current;
        }

        // BeginLoad assigns the new generation; every other file-scoped event
        // of a different generation is dropped.
        if (evt is not BeginLoadEvent && !evt.IsEngineScoped && evt.Generation != current.PlaybackGeneration)
        {
            return current;
        }

        PlaybackSnapshot next = evt switch
        {
            EngineInitializedEvent => current with { EngineLifecycle = EngineLifecycle.Initialized },
            EngineDisposingEvent => Freeze(current, EngineLifecycle.Disposing),
            EngineDisposedEvent => Freeze(current, EngineLifecycle.Disposed),
            EngineFaultedEvent e => current with
            {
                EngineLifecycle = EngineLifecycle.Faulted,
                Error = e.Error,
            },
            BeginLoadEvent e => BeginLoad(current, e.Generation),
            StartFileEvent => current with { MediaPhase = MediaPhase.Opening, Error = null },
            FileLoadedEvent => current with
            {
                MediaPhase = MediaPhase.Loaded,
                Activity = ClearTransient(current.Activity),
                // pause observe before StartFile is generation 0 and gets dropped.
                // File start is playing unless a later PauseChangedEvent says otherwise.
                PlaybackIntent = PlaybackIntent.Playing,
            },
            PlaybackRestartEvent => current with { MediaPhase = MediaPhase.Loaded, Activity = ClearTransient(current.Activity) },
            EndFileEvent e => ApplyEndFile(current, e),
            EofReachedEvent e => ApplyEofReached(current, e),
            IdleActiveEvent e => ApplyIdle(current, e),
            PauseChangedEvent e => current with
            {
                PlaybackIntent = e.Paused ? PlaybackIntent.Paused : PlaybackIntent.Playing,
            },
            PausedForCacheEvent e => ApplyCache(current, e),
            SeekingEvent e => current with
            {
                Activity = e.Seeking ? Activity.Seeking : ClearIf(current.Activity, Activity.Seeking),
            },
            VideoReconfigEvent => current with { Activity = Activity.Reconfiguring },
            VoConfiguredEvent e => current with
            {
                Activity = e.Configured ? ClearIf(current.Activity, Activity.Reconfiguring) : Activity.Reconfiguring,
            },
            PositionChangedEvent e => current.MediaPhase == MediaPhase.Empty
                ? current
                : current with { Position = e.Position },
            DurationChangedEvent e => current with { Duration = e.Duration },
            TracksChangedEvent e => current with { Tracks = e.Tracks },
            ChaptersChangedEvent e => current with { Chapters = e.Chapters },
            VideoParamsChangedEvent e => current with { VideoParams = e.VideoParams },
            OutputParamsChangedEvent e => current with { OutputParams = e.OutputParams },
            _ => current,
        };

        return Normalize(next);
    }

    private static PlaybackSnapshot BeginLoad(PlaybackSnapshot current, long generation) =>
        current with
        {
            PlaybackGeneration = generation,
            MediaPhase = MediaPhase.Opening,
            Activity = Activity.None,
            Position = null,
            Duration = null,
            Error = null,
            BufferingPercent = null,
            PausedForCache = false,
            Tracks = [],
            Chapters = [],
            VideoParams = null,
            OutputParams = null,
        };

    private static PlaybackSnapshot ApplyIdle(PlaybackSnapshot current, IdleActiveEvent evt)
    {
        if (!evt.Idle)
        {
            return current;
        }

        // Opening + idle is the gap between BeginLoad and START_FILE; do not wipe it.
        return current.MediaPhase is MediaPhase.Loaded or MediaPhase.Ended or MediaPhase.Failed
            ? EmptyMedia(current)
            : current;
    }

    private static PlaybackSnapshot ApplyEndFile(PlaybackSnapshot current, EndFileEvent evt)
    {
        return evt.Reason switch
        {
            EndFileReason.Error => current with
            {
                MediaPhase = MediaPhase.Failed,
                Activity = Activity.None,
                Error = evt.Error ?? "Playback failed.",
                PausedForCache = false,
                BufferingPercent = null,
            },
            EndFileReason.Stop => EmptyMedia(current),
            _ => current with
            {
                MediaPhase = MediaPhase.Ended,
                Activity = Activity.None,
                PausedForCache = false,
                BufferingPercent = null,
            },
        };
    }

    private static PlaybackSnapshot ApplyEofReached(PlaybackSnapshot current, EofReachedEvent evt)
    {
        if (evt.Reached)
        {
            // Only a file that actually played can reach EOF; Opening/Empty stay put.
            return current.MediaPhase is MediaPhase.Loaded or MediaPhase.Ended
                ? current with
                {
                    MediaPhase = MediaPhase.Ended,
                    Activity = Activity.None,
                    PausedForCache = false,
                    BufferingPercent = null,
                }
                : current;
        }

        // keep-open: seeking back from the end resumes the same file.
        return current.MediaPhase == MediaPhase.Ended
            ? current with { MediaPhase = MediaPhase.Loaded }
            : current;
    }

    private static PlaybackSnapshot ApplyCache(PlaybackSnapshot current, PausedForCacheEvent evt)
    {
        if (current.MediaPhase == MediaPhase.Empty)
        {
            return current;
        }

        if (evt.PausedForCache)
        {
            return current with
            {
                Activity = Activity.Buffering,
                PausedForCache = true,
                BufferingPercent = evt.BufferingPercent,
            };
        }

        return current with
        {
            Activity = ClearIf(current.Activity, Activity.Buffering),
            PausedForCache = false,
            BufferingPercent = null,
        };
    }

    private static PlaybackSnapshot EmptyMedia(PlaybackSnapshot current) =>
        current with
        {
            MediaPhase = MediaPhase.Empty,
            Activity = Activity.None,
            Position = null,
            Duration = null,
            Error = null,
            BufferingPercent = null,
            PausedForCache = false,
            Tracks = [],
            Chapters = [],
            VideoParams = null,
            OutputParams = null,
        };

    private static PlaybackSnapshot Freeze(PlaybackSnapshot current, EngineLifecycle lifecycle) =>
        current with { EngineLifecycle = lifecycle, Activity = Activity.None };

    private static Activity ClearTransient(Activity activity) =>
        activity is Activity.Reconfiguring ? Activity.None : activity;

    private static Activity ClearIf(Activity activity, Activity match) =>
        activity == match ? Activity.None : activity;

    private static PlaybackSnapshot Normalize(PlaybackSnapshot snapshot)
    {
        PlaybackSnapshot next = snapshot;

        if (next.MediaPhase == MediaPhase.Empty)
        {
            next = next with
            {
                Activity = Activity.None,
                Position = null,
                BufferingPercent = null,
                PausedForCache = false,
            };
        }

        if (next.Activity == Activity.Buffering && !next.PausedForCache)
        {
            next = next with { Activity = Activity.None, BufferingPercent = null };
        }

        if (next.Activity != Activity.Buffering)
        {
            next = next with { BufferingPercent = null };
        }

        IReadOnlyList<string> violations = next.InvariantViolations();
        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                "PlaybackSnapshot invariant violated: " + string.Join(" ", violations));
        }

        return next;
    }
}
