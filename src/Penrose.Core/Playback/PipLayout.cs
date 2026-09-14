namespace Penrose.Core.Playback;

/// <summary>Compact-overlay / mini-player size from source aspect. Width-capped.</summary>
public static class PipLayout
{
    public const int DefaultMaxWidth = 400;

    public static (int Width, int Height) SizeFor(int videoWidth, int videoHeight, int maxWidth = DefaultMaxWidth)
    {
        double aspect = videoWidth > 0 && videoHeight > 0
            ? videoWidth / (double)videoHeight
            : 16.0 / 9.0;
        int width = Math.Clamp(maxWidth, 160, 500);
        int height = (int)Math.Round(width / aspect);
        if (height < 90)
        {
            height = 90;
            width = (int)Math.Round(height * aspect);
        }

        if (height > 280)
        {
            height = 280;
            width = (int)Math.Round(height * aspect);
        }

        return (Math.Clamp(width, 160, 500), Math.Clamp(height, 90, 300));
    }
}
