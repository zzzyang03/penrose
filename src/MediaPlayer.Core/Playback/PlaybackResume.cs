namespace MediaPlayer.Core.Playback;

/// <summary>
/// Resume only from a mid-file position: a saved position inside the last few
/// seconds means the file was effectively finished, and a position under
/// <see cref="MinStartMs"/> is not worth a visible jump. (The historical
/// "loadfile start= is InvalidParameter" note was the options-in-index-slot bug,
/// fixed in LoadfileOptions, not an EOF condition.)
/// </summary>
public static class PlaybackResume
{
    public const int MinStartMs = 3000;
    public const int EndGraceMs = 4000;

    public static bool ShouldRestore(long positionMs, long? durationMs)
    {
        if (positionMs < MinStartMs)
        {
            return false;
        }

        return !IsNearEnd(positionMs, durationMs);
    }

    public static bool IsNearEnd(long positionMs, long? durationMs) =>
        durationMs is long duration && duration > 0 && positionMs >= Math.Max(0, duration - EndGraceMs);
}
