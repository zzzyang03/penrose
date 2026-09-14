using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Penrose.Core.Settings;
using Penrose.Core.Sources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Penrose.App.WinUI;

/// <summary>One playlist entry as shown in the playlist pane.</summary>
public sealed class PlaylistRow
{
    public PlaylistRow(int index, string name, bool current)
    {
        Index = index;
        Name = name;
        IsCurrent = current;
    }

    public int Index { get; }

    public string Name { get; }

    public bool IsCurrent { get; }

    /// <summary>Play glyph for the current entry, otherwise the 1-based position.</summary>
    public string Indicator => IsCurrent ? "\uE768" : (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string IndicatorFont => IsCurrent ? "Segoe Fluent Icons" : "Segoe UI";

    public double Opacity => IsCurrent ? 1.0 : 0.72;
}

/// <summary>One tile of the Emby poster wall.</summary>
public sealed class PosterRow : INotifyPropertyChanged
{
    private readonly Uri? _image;
    private BitmapImage? _bitmap;
    private double _tileWidth;
    private double _imageHeight;

    public PosterRow(LibraryItem item, string title, string subtitle, Uri? image, double tileWidth, double imageHeight, string resumeLabel = "")
    {
        Item = item;
        Title = title;
        Subtitle = subtitle;
        _image = image;
        _tileWidth = tileWidth;
        _imageHeight = imageHeight;
        ResumeLabel = resumeLabel;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LibraryItem Item { get; }

    public string Title { get; }

    public string Subtitle { get; }

    /// <summary>"继续观看 · 剩 12 分钟" on partially watched items.</summary>
    public string ResumeLabel { get; }

    /// <summary>Re-fitted by the wall when the window width changes (OneWay bindings).</summary>
    public double TileWidth
    {
        get => _tileWidth;
        private set
        {
            if (Math.Abs(_tileWidth - value) > 0.5)
            {
                _tileWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileWidth)));
            }
        }
    }

    public double ImageHeight
    {
        get => _imageHeight;
        private set
        {
            if (Math.Abs(_imageHeight - value) > 0.5)
            {
                _imageHeight = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageHeight)));
            }
        }
    }

    public void Resize(double tileWidth, double imageHeight)
    {
        TileWidth = tileWidth;
        ImageHeight = imageHeight;
    }

    /// <summary>
    /// Created on first bind, i.e. when the tile is realised, so a 500-item folder
    /// does not start 500 downloads at once. Decoded at 2× the tile for HiDPI.
    /// </summary>
    public ImageSource? Image
    {
        get
        {
            if (_image is null)
            {
                return null;
            }

            return _bitmap ??= PosterImageCache.Get(_image, decodeWidth: (int)(TileWidth * 2));
        }
    }

    public Visibility PlaceholderVisibility => _image is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Folder-like kinds get a folder glyph, playable items a film glyph.</summary>
    public string PlaceholderGlyph => Item.IsFolder ? "\uE8B7" : "\uE714";

    public double Progress => Item.PlayedPercentage is { } p && p > 0 && p < 100 ? p : 0;

    public Visibility ProgressVisibility => Progress > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PlayedVisibility => Item.Played && Progress == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SubtitleVisibility => string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// Poster tiles that page themselves in: the GridView asks for more when the
/// user scrolls near the end. The loader returns the next page starting at the
/// current count and whether anything is left.
/// </summary>
public sealed class PosterCollection : ObservableCollection<PosterRow>, ISupportIncrementalLoading
{
    private readonly Func<int, CancellationToken, Task<(IReadOnlyList<PosterRow> Rows, bool More)>> _load;
    private readonly Action<Exception> _onError;
    private bool _busy;

    public PosterCollection(
        IEnumerable<PosterRow> firstPage,
        bool more,
        Func<int, CancellationToken, Task<(IReadOnlyList<PosterRow> Rows, bool More)>> load,
        Action<Exception> onError)
        : base(firstPage)
    {
        _load = load;
        _onError = onError;
        HasMoreItems = more;
    }

    public bool HasMoreItems { get; private set; }

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count) =>
        AsyncInfo.Run(async cancellationToken =>
        {
            if (_busy || !HasMoreItems)
            {
                return new LoadMoreItemsResult { Count = 0 };
            }

            _busy = true;
            try
            {
                (IReadOnlyList<PosterRow> rows, bool more) = await _load(Count, cancellationToken).ConfigureAwait(true);
                foreach (PosterRow row in rows)
                {
                    Add(row);
                }

                HasMoreItems = more && rows.Count > 0;
                return new LoadMoreItemsResult { Count = (uint)rows.Count };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                HasMoreItems = false;
                _onError(ex);
                return new LoadMoreItemsResult { Count = 0 };
            }
            finally
            {
                _busy = false;
            }
        });
}

/// <summary>One cast / crew card on the item page.</summary>
public sealed class PersonRow
{
    private readonly Uri? _image;
    private BitmapImage? _bitmap;

    public PersonRow(PersonInfo person, string role, Uri? image)
    {
        ArgumentNullException.ThrowIfNull(person);
        Person = person;
        Name = person.Name;
        Role = role;
        _image = image;
    }

    public PersonInfo Person { get; }

    public string Name { get; }

    public string Role { get; }

    public ImageSource? Image => _image is null
        ? null
        : _bitmap ??= PosterImageCache.Get(_image, decodeHeight: 332);
}

/// <summary>One backdrop in the item page's art strip.</summary>
public sealed class ArtRow
{
    private readonly Uri _image;
    private BitmapImage? _bitmap;

    public ArtRow(Uri image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _image = image;
    }

    public ImageSource Image => _bitmap ??= PosterImageCache.Get(_image, decodeWidth: 464);
}

/// <summary>A saved media server in the home page list.</summary>
public sealed class ServerRow
{
    public ServerRow(LibraryServerSettings server, bool active, string activeText, string removeText)
    {
        ArgumentNullException.ThrowIfNull(server);
        Server = server;
        Name = server.DisplayName;
        Detail = string.IsNullOrEmpty(server.UserName) ? server.Host : server.UserName + " @ " + server.Host;
        ActiveVisibility = active ? Visibility.Visible : Visibility.Collapsed;
        ActiveText = activeText;
        RemoveText = removeText;
    }

    public LibraryServerSettings Server { get; }

    public string Name { get; }

    public string Detail { get; }

    public Visibility ActiveVisibility { get; }

    public string ActiveText { get; }

    public string RemoveText { get; }

    /// <summary>Kind icon; only Emby has one so far.</summary>
    public ImageSource IconSource { get; } = new BitmapImage(new Uri("ms-appx:///Assets/Servers/emby.png"));
}

/// <summary>A recently played local file for the empty-state list.</summary>
public sealed class RecentRow
{
    public RecentRow(string path, string name, string detail)
    {
        Path = path;
        Name = name;
        Detail = detail;
    }

    public string Path { get; }

    public string Name { get; }

    public string Detail { get; }
}
