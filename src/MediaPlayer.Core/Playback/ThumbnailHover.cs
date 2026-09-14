namespace MediaPlayer.Core.Playback;

/// <summary>Maps seek-bar hover to a quantized preview time. Grabbing frames is the host's job.</summary>
public static class ThumbnailHover
{
    public const int DebounceMilliseconds = 90;

    public static TimeSpan? TimeAt(double x, double width, TimeSpan duration)
    {
        if (width <= 1 || duration <= TimeSpan.Zero || double.IsNaN(x))
        {
            return null;
        }

        double ratio = Math.Clamp(x / width, 0, 1);
        return TimeSpan.FromSeconds(ratio * duration.TotalSeconds);
    }

    public static TimeSpan Quantize(TimeSpan time, double stepSeconds = 0.5)
    {
        if (stepSeconds <= 0)
        {
            return time;
        }

        double seconds = Math.Max(0, time.TotalSeconds);
        double stepped = Math.Round(seconds / stepSeconds) * stepSeconds;
        return TimeSpan.FromSeconds(stepped);
    }

    public static bool IsLocalFile(Uri? uri) =>
        uri is { IsFile: true } && File.Exists(uri.LocalPath);
}
