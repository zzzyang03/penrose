using Penrose.Core.Playback;

namespace Penrose.Core.Sources;

/// <summary>
/// Local file / strm → <see cref="PlaybackRequest"/>. strm is untrusted
/// (<see cref="StrmParser"/>). Sidecar discovery is mpv <c>sub-auto=fuzzy</c>
/// plus <c>sub-file-paths=字幕;Subs;subs;subtitles</c>; extra IINA-style
/// matches are attached by <see cref="LocalFileMatcher"/>. This factory only attaches an
/// explicit extra subtitle when the caller passes one.
/// </summary>
public static class LocalPlaybackFactory
{
    public static PlaybackRequest FromUserInput(
        string input,
        TimeSpan? startPosition = null,
        IReadOnlyList<ExternalSubtitle>? extraSubtitles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        string trimmed = input.Trim().Trim('"');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            return new PlaybackRequest
            {
                RequestId = Guid.NewGuid(),
                Uri = uri,
                StartPosition = startPosition,
                ExternalSubtitles = extraSubtitles ?? [],
                SourceKind = MediaSourceKind.StrmRelay,
            };
        }

        string? disc = ResolveDiscRoot(trimmed);
        if (disc is not null)
        {
            return FromDisc(disc, startPosition, extraSubtitles);
        }

        return FromPath(trimmed, startPosition, extraSubtitles);
    }

    public static PlaybackRequest FromPath(
        string path,
        TimeSpan? startPosition = null,
        IReadOnlyList<ExternalSubtitle>? extraSubtitles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("Media file not found.", full);
        }

        Uri uri;
        MediaSourceKind sourceKind;
        if (IsStrm(full))
        {
            uri = ReadStrm(full);
            // strm is "direct" when it points at a file the player can open itself
            // (file:// or an SMB UNC), and "relay" when it points at an http(s)
            // URL the player must fetch.
            sourceKind = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
                ? MediaSourceKind.StrmRelay
                : MediaSourceKind.StrmDirect;
        }
        else
        {
            uri = new Uri(full);
            // UNC paths the player opens as a local file (for example
            // \\server\share\foo.mkv) are treated as a network share here; a
            // plain drive letter is just a local file.
            sourceKind = uri.IsUnc ? MediaSourceKind.NetworkShare : MediaSourceKind.LocalFile;
        }

        return new PlaybackRequest
        {
            RequestId = Guid.NewGuid(),
            Uri = uri,
            StartPosition = startPosition,
            ExternalSubtitles = extraSubtitles ?? [],
            SourceKind = sourceKind,
        };
    }

    public static bool IsIsoPath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase);

    public static bool IsDiscDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Equals("BDMV", StringComparison.OrdinalIgnoreCase)
            || name.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Directory.Exists(Path.Combine(path, "BDMV"))
            || Directory.Exists(Path.Combine(path, "VIDEO_TS"));
    }

    public static string? ResolveDiscRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full = Path.GetFullPath(path.Trim().Trim('"'));
        if (File.Exists(full) && IsIsoPath(full))
        {
            return full;
        }

        if (!Directory.Exists(full))
        {
            return null;
        }

        string name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Equals("BDMV", StringComparison.OrdinalIgnoreCase)
            || name.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(full);
        }

        if (Directory.Exists(Path.Combine(full, "BDMV"))
            || Directory.Exists(Path.Combine(full, "VIDEO_TS")))
        {
            return full;
        }

        return null;
    }

    public static PlaybackRequest FromDisc(
        string path,
        TimeSpan? startPosition = null,
        IReadOnlyList<ExternalSubtitle>? extraSubtitles = null,
        int? discTitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        Uri uri = File.Exists(full)
            ? new Uri(full)
            : new Uri(AppendDirectorySeparator(full));
        return new PlaybackRequest
        {
            RequestId = Guid.NewGuid(),
            Uri = uri,
            StartPosition = startPosition,
            ExternalSubtitles = extraSubtitles ?? [],
            DiscTitle = discTitle,
            SourceKind = MediaSourceKind.LocalDisc,
        };
    }

    public static bool IsSubtitlePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string ext = Path.GetExtension(path);
        return ext.Equals(".ass", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ssa", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".srt", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".vtt", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".sub", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMediaPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (IsSubtitlePath(path))
        {
            return false;
        }

        return MediaExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// Non-recursive listing of Tier A/B media files, sorted by name.
    /// Hidden/system entries and sidecar subtitles are skipped.
    /// </summary>
    public static IReadOnlyList<string> EnumerateMediaFiles(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string full = Path.GetFullPath(directory);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException(full);
        }

        List<string> files = [];
        foreach (string path in Directory.EnumerateFiles(full))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.StartsWith("._", StringComparison.Ordinal))
            {
                continue;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                continue;
            }

            if (IsMediaPath(path))
            {
                files.Add(path);
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public static int IndexOfPath(IReadOnlyList<string> files, string path)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        for (int i = 0; i < files.Count; i++)
        {
            if (string.Equals(files[i], full, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".mov", ".webm", ".ts", ".m2ts", ".mts", ".mpg", ".mpeg",
        ".avi", ".wmv", ".asf", ".flv", ".ogv", ".iso", ".strm",
        ".mka", ".flac", ".mp3", ".aac", ".m4a", ".opus", ".ogg", ".wav",
        ".ac3", ".eac3", ".dts", ".thd",
    };

    private static string AppendDirectorySeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static bool IsStrm(string path) =>
        Path.GetExtension(path).Equals(".strm", StringComparison.OrdinalIgnoreCase);

    private static Uri ReadStrm(string path)
    {
        FileInfo info = new(path);
        if (info.Length > StrmParser.MaxBytes)
        {
            throw new InvalidOperationException("strm exceeds the " + StrmParser.MaxBytes + " byte cap.");
        }

        string text = File.ReadAllText(path);
        return StrmParser.TryParse(text)
            ?? throw new InvalidOperationException("strm has no whitelisted URL or UNC path.");
    }
}
