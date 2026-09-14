namespace Penrose.VideoSurface;

/// <summary>
/// DIP size and composition scale are stored separately. Swapchain pixels =
/// DIP × CompositionScale. WinUI must handle CompositionScaleChanged.
/// </summary>
public readonly record struct SurfaceLayout(
    double LogicalWidthDip,
    double LogicalHeightDip,
    double CompositionScaleX,
    double CompositionScaleY,
    double RasterizationScale)
{
    public int PixelWidth => ToPixels(LogicalWidthDip, CompositionScaleX);

    public int PixelHeight => ToPixels(LogicalHeightDip, CompositionScaleY);

    public string CompositionSize => $"{PixelWidth}x{PixelHeight}";

    public bool IsEmpty => PixelWidth <= 0 || PixelHeight <= 0;

    private static int ToPixels(double dip, double scale)
    {
        if (dip <= 0 || scale <= 0)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero));
    }
}
