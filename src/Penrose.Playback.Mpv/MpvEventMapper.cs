using Penrose.Core.Playback;
using Penrose.Interop.LibMpv;

namespace Penrose.Playback.Mpv;

internal static class MpvEventMapper
{
    public static PlaybackEvent? ToPlaybackEvent(MpvClientEvent evt, long generation)
    {
        return evt.Id switch
        {
            MpvEventId.StartFile => new StartFileEvent { Generation = generation },
            MpvEventId.FileLoaded => new FileLoadedEvent { Generation = generation },
            MpvEventId.PlaybackRestart => new PlaybackRestartEvent { Generation = generation },
            MpvEventId.EndFile => new EndFileEvent
            {
                Generation = generation,
                Reason = evt.EndFileReason switch
                {
                    MpvEndFileReason.Eof => EndFileReason.Eof,
                    MpvEndFileReason.Stop or MpvEndFileReason.Quit => EndFileReason.Stop,
                    MpvEndFileReason.Error => EndFileReason.Error,
                    MpvEndFileReason.Redirect => EndFileReason.Redirect,
                    _ => EndFileReason.Unknown,
                },
                Error = evt.ErrorString,
            },
            MpvEventId.VideoReconfig => new VideoReconfigEvent { Generation = generation },
            MpvEventId.Seek => new SeekingEvent { Generation = generation, Seeking = true },
            MpvEventId.PropertyChange => MapProperty(evt, generation),
            _ => null,
        };
    }

    private static PlaybackEvent? MapProperty(MpvClientEvent evt, long generation)
    {
        return evt.PropertyName switch
        {
            "pause" => new PauseChangedEvent
            {
                Generation = generation,
                Paused = ReadFlag(evt),
            },
            "paused-for-cache" => new PausedForCacheEvent
            {
                Generation = generation,
                PausedForCache = ReadFlag(evt),
            },
            "cache-buffering-state" => new PausedForCacheEvent
            {
                Generation = generation,
                PausedForCache = true,
                BufferingPercent = int.TryParse(evt.PropertyString, out int percent) ? percent : null,
            },
            "seeking" => new SeekingEvent
            {
                Generation = generation,
                Seeking = ReadFlag(evt),
            },
            "idle-active" => new IdleActiveEvent
            {
                Generation = generation,
                Idle = ReadFlag(evt),
            },
            "eof-reached" => new EofReachedEvent
            {
                Generation = generation,
                Reached = ReadFlag(evt),
            },
            "vo-configured" => new VoConfiguredEvent
            {
                Generation = generation,
                Configured = ReadFlag(evt),
            },
            "time-pos" => new PositionChangedEvent
            {
                Generation = generation,
                Position = ParseSeconds(evt.PropertyString),
            },
            "duration" => new DurationChangedEvent
            {
                Generation = generation,
                Duration = ParseSeconds(evt.PropertyString),
            },
            "track-list" => new TracksChangedEvent
            {
                Generation = generation,
                Tracks = TrackListParser.Parse(evt.PropertyString),
            },
            _ => null,
        };
    }

    /// <summary>
    /// Reads a boolean property without assuming which format it was observed
    /// under: <c>PropertyFlag</c> is only populated for MPV_FORMAT_FLAG, so a
    /// property observed as a string must fall back to its text. An unavailable
    /// property carries neither and reads as false.
    /// </summary>
    private static bool ReadFlag(MpvClientEvent evt)
    {
        if (evt.PropertyFlag is { } flag)
        {
            return flag;
        }

        return evt.PropertyString switch
        {
            "yes" or "true" or "1" => true,
            _ => false,
        };
    }

    private static TimeSpan? ParseSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }
}
