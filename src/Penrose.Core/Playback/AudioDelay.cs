using System.Globalization;

namespace Penrose.Core.Playback;

/// <summary>
/// mpv <c>audio-delay</c> helpers for the per-file A/V sync nudge. Positive seconds
/// play the sound later than the picture, negative seconds earlier.
/// </summary>
public static class AudioDelay
{
    /// <summary>One key press or menu step, in seconds.</summary>
    public const double Step = 0.05;

    /// <summary><see cref="Step"/> in whole milliseconds, for labels.</summary>
    public static int StepMilliseconds => (int)Math.Round(Step * 1000);

    /// <summary>
    /// Adds <paramref name="delta"/> and rounds to the millisecond, so repeated steps
    /// land on 0.15 rather than 0.15000000000000002.
    /// </summary>
    public static double Nudge(double current, double delta) => Math.Round(current + delta, 3);

    /// <summary>Reads mpv's <c>audio-delay</c> string ("0.050000"); a missing or unparsable value counts as in sync.</summary>
    public static double Parse(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds)
            ? seconds
            : 0;

    /// <summary>Property value for mpv: "0.05", "-0.1", "0".</summary>
    public static string ToProperty(double seconds) =>
        Math.Round(seconds, 3).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Signed millisecond label: "+50 ms", "-1250 ms", "0 ms".</summary>
    public static string FormatMilliseconds(double seconds)
    {
        long milliseconds = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
        return milliseconds.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
    }
}
