using Microsoft.UI.Xaml.Media.Imaging;

namespace MediaPlayer.App.WinUI;

/// <summary>
/// Reuses decoded poster bitmaps for the same Emby image URL so navigating
/// back through a library does not start the download again.
/// </summary>
internal static class PosterImageCache
{
    private const int Limit = 256;
    private static readonly Dictionary<string, BitmapImage> Images = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LinkedListNode<string>> Nodes = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> Order = new();
    private static readonly object Gate = new();

    public static BitmapImage Get(Uri uri, int decodeWidth = 0, int decodeHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(uri);
        string key = uri.AbsoluteUri + "|" + decodeWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "x" + decodeHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (Gate)
        {
            if (Images.TryGetValue(key, out BitmapImage? existing))
            {
                Touch(key);
                return existing;
            }

            BitmapImage image = new()
            {
                DecodePixelType = DecodePixelType.Logical,
            };
            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }

            if (decodeHeight > 0)
            {
                image.DecodePixelHeight = decodeHeight;
            }

            image.UriSource = uri;
            Images[key] = image;
            Nodes[key] = Order.AddFirst(key);
            Evict();
            return image;
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Images.Clear();
            Nodes.Clear();
            Order.Clear();
        }
    }

    private static void Touch(string key)
    {
        if (Nodes.TryGetValue(key, out LinkedListNode<string>? node))
        {
            Order.Remove(node);
            Nodes[key] = Order.AddFirst(key);
        }
    }

    private static void Evict()
    {
        while (Order.Count > Limit && Order.Last is { } last)
        {
            Order.RemoveLast();
            Nodes.Remove(last.Value);
            Images.Remove(last.Value);
        }
    }
}
