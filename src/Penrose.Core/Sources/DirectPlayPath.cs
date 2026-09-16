using Penrose.Core.Playback;

namespace Penrose.Core.Sources;

/// <summary>
/// Emby/Jellyfin <c>Protocol=File</c> paths are the server's filesystem.
/// Only use them when this machine can actually open the file, and only when
/// the user has opted in: a server-controlled UNC path makes the client open an
/// SMB session to an arbitrary host, so it is never probed by default.
/// </summary>
public static class DirectPlayPath
{
    public static bool TryOpenLocal(string? protocol, string? path, out Uri uri) =>
        TryOpenLocal(protocol, path, allowServerFilePaths: false, out uri);

    public static bool TryOpenLocal(string? protocol, string? path, bool allowServerFilePaths, out Uri uri)
    {
        uri = null!;
        if (!allowServerFilePaths || !LooksLikeFile(protocol, path) || path is null)
        {
            return false;
        }

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            uri = new Uri(Path.GetFullPath(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or UriFormatException)
        {
            return false;
        }
    }

    public static bool LooksLikeFile(string? protocol, string? path) =>
        string.Equals(protocol, "File", StringComparison.OrdinalIgnoreCase)
        && path is not null
        && (path.StartsWith(@"\\", StringComparison.Ordinal) || Path.IsPathRooted(path));

    /// <summary>
    /// Absolute http(s) URL, or a server-relative path (<c>/videos/...</c>). Only
    /// valid for fields the server documents as URLs (<c>DirectStreamUrl</c>,
    /// <c>TranscodingUrl</c>); a MediaSource <c>Path</c> that starts with '/' is a
    /// Linux filesystem path, not a URL — use <see cref="IsHttpUrl"/> for those.
    /// </summary>
    public static bool LooksLikeRemoteUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (IsHttpUrl(value) || value.StartsWith('/'));

    public static bool IsHttpUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// strm items put the real media URL in <c>Path</c> (OpenList, a cloud
    /// drive, another host). Playing that URL lets mpv follow the 302 itself.
    /// The server <c>DirectStreamUrl</c> is a same-origin proxy: it often cannot
    /// Range-seek a cloud strm, so a short lavf probe misses audio / Dolby Vision
    /// metadata and a resume lands mid-GOP.
    /// <c>Path</c> is ignored when it is loopback (only the server can open it) or
    /// the same origin as the server (a Download route, not a strm target).
    /// </summary>
    public static bool TryChooseRemotePlay(
        string? path,
        string? directStreamUrl,
        Uri serverBase,
        out string url,
        out MediaSourceKind kind)
    {
        ArgumentNullException.ThrowIfNull(serverBase);
        url = "";
        kind = MediaSourceKind.ServerDirectPlay;
        if (IsExternalHttpPath(path, serverBase))
        {
            url = path!;
            kind = MediaSourceKind.StrmRelay;
            return true;
        }

        if (LooksLikeRemoteUrl(directStreamUrl))
        {
            url = directStreamUrl!;
            kind = MediaSourceKind.ServerDirectPlay;
            return true;
        }

        if (IsHttpUrl(path))
        {
            url = path!;
            kind = MediaSourceKind.ServerDirectPlay;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Absolute http(s) URL whose host is not the media server and not loopback.
    /// OpenList on the same machine uses a different port, which is a different
    /// origin and still counts.
    /// </summary>
    public static bool IsExternalHttpPath(string? path, Uri serverBase)
    {
        ArgumentNullException.ThrowIfNull(serverBase);
        if (!IsHttpUrl(path) || !Uri.TryCreate(path, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return !uri.IsLoopback && !HttpQueryAuth.IsSameOrigin(uri, serverBase);
    }
}
