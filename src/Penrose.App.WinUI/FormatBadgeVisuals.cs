using System;
using Penrose.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Penrose.App.WinUI;

/// <summary>
/// Builds the premium-format chips shown when a file starts: a frosted grey pill
/// carrying the format's white logo (Assets/Badges). Every chip has the same box
/// height; the single-line marks (HDR10+, DTS:X) are trimmed and given a wider
/// box so their visual weight matches the two-line Dolby lock-ups.
/// </summary>
internal static class FormatBadgeVisuals
{
    /// <summary>Large chip for the transient strip.</summary>
    public static Border CreateChip(string badge) => Build(badge, compact: false);

    /// <summary>Small chip that stays next to the title after the strip has faded.</summary>
    public static Border CreateTitleChip(string badge) => Build(badge, compact: true);

    private static Border Build(string badge, bool compact)
    {
        ResourceDictionary res = Application.Current.Resources;
        double scale = compact ? 12.0 / 36.0 : 1;
        FrameworkElement content = Logo(badge) is { } logo
            ? new Image
            {
                Source = new BitmapImage(new Uri("ms-appx:///Assets/Badges/" + logo.File)),
                Height = 36 * scale,
                Width = logo.Width * scale,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new TextBlock
            {
                Text = badge,
                Style = (Style)res[compact ? "TitleBadgeText" : "BadgeText"],
                VerticalAlignment = VerticalAlignment.Center,
            };

        return new Border
        {
            Style = (Style)res[compact ? "TitleBadgeChip" : "BadgeChip"],
            Child = content,
        };
    }

    /// <summary>Box width at the 36-dip chip height (Uniform stretch centres the mark).</summary>
    private static (string File, double Width)? Logo(string badge) => badge switch
    {
        FormatBadges.DolbyVision => ("dolby-vision.png", 96),
        FormatBadges.DolbyAtmos => ("dolby-atmos.png", 96),
        FormatBadges.Hdr10Plus => ("hdr10plus.png", 108),
        FormatBadges.DtsX => ("dts-x.png", 100),
        _ => null,
    };
}
