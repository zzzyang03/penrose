namespace MediaPlayer.Core.Playback;

/// <summary>
/// Emby/Jellyfin tokens for HTTP media that cannot take loadfile
/// <c>http-header-fields</c> (mpv <c>sub-add</c>). Query <c>api_key</c> is the
/// documented fallback when header options are InvalidParameter.
/// </summary>
public static class HttpQueryAuth
{
    /// <summary>
    /// Appends the token from <paramref name="headers"/> as <c>api_key</c>. When
    /// <paramref name="origin"/> is given, the token is only attached to URLs on
    /// that scheme/host/port: a server-supplied subtitle or media path pointing at
    /// a third-party host must never receive the server's credential.
    /// </summary>
    public static Uri Apply(Uri uri, IReadOnlyDictionary<string, string> headers, Uri? origin = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(headers);
        if (!IsHttp(uri))
        {
            return uri;
        }

        if (origin is not null && !IsSameOrigin(uri, origin))
        {
            return uri;
        }

        string? token = TryGetToken(headers);
        if (string.IsNullOrWhiteSpace(token))
        {
            return uri;
        }

        if (uri.Query.Contains("api_key=", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        UriBuilder builder = new(uri);
        string existing = builder.Query.TrimStart('?');
        string pair = "api_key=" + Uri.EscapeDataString(token);
        builder.Query = string.IsNullOrEmpty(existing) ? pair : existing + "&" + pair;
        return builder.Uri;
    }

    public static string? TryGetToken(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.Count == 0)
        {
            return null;
        }

        if (TryHeader(headers, "X-Emby-Token", out string? emby)
            || TryHeader(headers, "X-MediaBrowser-Token", out emby))
        {
            return emby;
        }

        if (!TryHeader(headers, "Authorization", out string? authorization))
        {
            return null;
        }

        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            string bearer = authorization["Bearer ".Length..].Trim();
            return string.IsNullOrWhiteSpace(bearer) ? null : bearer;
        }

        int tokenAt = authorization.IndexOf("Token=", StringComparison.OrdinalIgnoreCase);
        if (tokenAt < 0)
        {
            return null;
        }

        string rest = authorization[(tokenAt + "Token=".Length)..].Trim();
        if (rest.StartsWith('"'))
        {
            int end = rest.IndexOf('"', 1);
            return end > 1 ? rest[1..end] : rest.Trim('"');
        }

        int cut = rest.IndexOfAny([',', ' ']);
        string value = cut < 0 ? rest : rest[..cut];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static bool IsReadableFile(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsFile)
        {
            return false;
        }

        try
        {
            return File.Exists(uri.LocalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Scheme, host and (effective) port match. Path and query are irrelevant.</summary>
    public static bool IsSameOrigin(Uri a, Uri b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.IsAbsoluteUri
            && b.IsAbsoluteUri
            && a.Scheme.Equals(b.Scheme, StringComparison.OrdinalIgnoreCase)
            && a.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase)
            && a.Port == b.Port;
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool TryHeader(
        IReadOnlyDictionary<string, string> headers,
        string name,
        out string value)
    {
        foreach ((string key, string header) in headers)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(header))
            {
                value = header.Trim();
                return true;
            }
        }

        value = "";
        return false;
    }
}
