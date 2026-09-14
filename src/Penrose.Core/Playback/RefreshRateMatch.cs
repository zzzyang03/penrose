namespace Penrose.Core.Playback;

/// <summary>
/// Pick a display refresh that is a clean multiple of the source fps.
/// 24 fps on 60 Hz is 2.5:1 (judder); 24 or 48 Hz is preferred when present.
/// </summary>
public static class RefreshRateMatch
{
    public static int SnapSourceFps(double sourceFps)
    {
        if (sourceFps is < 10 or > 240)
        {
            return 0;
        }

        int[] marks = [24, 25, 30, 48, 50, 60, 72, 100, 120, 144, 165, 240];
        int best = 0;
        double bestDelta = double.MaxValue;
        foreach (int mark in marks)
        {
            double delta = Math.Min(Math.Abs(sourceFps - mark), Math.Abs(sourceFps - mark * 1000.0 / 1001.0));
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = mark;
            }
        }

        return bestDelta <= 0.6 ? best : (int)Math.Round(sourceFps);
    }

    public static bool AlreadyMatched(int currentHz, int wantedHz)
    {
        if (wantedHz <= 0 || currentHz <= 0)
        {
            return true;
        }

        if (Math.Abs(currentHz - wantedHz) <= 1)
        {
            return true;
        }

        double ratio = currentHz / (double)wantedHz;
        int nearest = (int)Math.Round(ratio);
        return nearest >= 1 && Math.Abs(ratio - nearest) < 0.03;
    }

    public static int? Pick(double sourceFps, int currentHz, IReadOnlyList<int> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        int wanted = SnapSourceFps(sourceFps);
        if (wanted <= 0)
        {
            return null;
        }

        if (AlreadyMatched(currentHz, wanted))
        {
            return null;
        }

        foreach (int candidate in Ladder(wanted))
        {
            int match = available.FirstOrDefault(hz => Math.Abs(hz - candidate) <= 1);
            if (match > 0)
            {
                return match == currentHz ? null : match;
            }
        }

        return null;
    }

    private static IEnumerable<int> Ladder(int wanted)
    {
        yield return wanted;
        if (wanted <= 30)
        {
            yield return wanted * 2;
        }

        if (wanted == 24)
        {
            yield return 48;
            yield return 72;
            yield return 120;
        }
        else if (wanted == 25)
        {
            yield return 50;
            yield return 100;
        }
        else if (wanted == 30)
        {
            yield return 60;
            yield return 120;
        }
        else if (wanted == 60)
        {
            yield return 120;
        }
    }
}
