using System.Text.RegularExpressions;

namespace MediaPlayer.Diagnostics;

public static partial class LogRedactor
{
    public const string Mask = "***";

    public static string Redact(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message ?? string.Empty;
        }

        string text = message;
        text = Authorization().Replace(text, "$1" + Mask);
        text = Bearer().Replace(text, "Bearer " + Mask);
        text = CookieHeader().Replace(text, "$1" + Mask);
        text = EmbyTokenHeader().Replace(text, "$1" + Mask);
        text = MediaBrowserToken().Replace(text, "$1" + Mask);
        text = AccessTokenJson().Replace(text, "$1" + Mask);
        text = JsonSecret().Replace(text, "$1" + Mask);
        text = QuerySecret().Replace(text, "$1" + Mask);
        text = NamedSecret().Replace(text, "$1" + Mask);
        text = UncUserInfo().Replace(text, @"\\***@");
        text = UriUserInfo().Replace(text, "$1://***@");
        return text;
    }

    public static bool ContainsSensitive(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return Authorization().IsMatch(message)
            || Bearer().IsMatch(message)
            || CookieHeader().IsMatch(message)
            || EmbyTokenHeader().IsMatch(message)
            || MediaBrowserToken().IsMatch(message)
            || AccessTokenJson().IsMatch(message)
            || JsonSecret().IsMatch(message)
            || QuerySecret().IsMatch(message)
            || NamedSecret().IsMatch(message)
            || UncUserInfo().IsMatch(message)
            || UriUserInfo().IsMatch(message);
    }

    [GeneratedRegex(@"(Authorization:\s*).+$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex Authorization();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"(Cookie:\s*).+$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CookieHeader();

    [GeneratedRegex(@"(X-(?:Emby|MediaBrowser)-Token:\s*).+$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex EmbyTokenHeader();

    /// <summary>X-Emby-Authorization: MediaBrowser Client="…", Token="…"</summary>
    [GeneratedRegex(@"(\bToken\s*=\s*"")[^""]*", RegexOptions.IgnoreCase)]
    private static partial Regex MediaBrowserToken();

    [GeneratedRegex(@"(""AccessToken""\s*:\s*"")[^""]+", RegexOptions.IgnoreCase)]
    private static partial Regex AccessTokenJson();

    /// <summary>JSON-quoted keys: "Token":"…", "ApiKey":"…", "Password":"…", "Pw":"…".</summary>
    [GeneratedRegex(@"(""(?:token|access_token|api[_-]?key|apikey|password|pw|secret)""\s*:\s*"")[^""]*", RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecret();

    /// <summary>Query parameters: ?api_key=…&amp;… — stop at the next separator so the URL survives.</summary>
    [GeneratedRegex(@"([?&](?:api[_-]?key|apikey|token|access_token|password|pw)=)[^&\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex QuerySecret();

    [GeneratedRegex(@"\b(token|access_token|api[_-]?key|apikey|password|pw|secret)\s*[=:]\s*[^\s&,;""']+", RegexOptions.IgnoreCase)]
    private static partial Regex NamedSecret();

    [GeneratedRegex(@"\\\\[^\\/]+@")]
    private static partial Regex UncUserInfo();

    [GeneratedRegex(@"([a-zA-Z][a-zA-Z0-9+.-]*)://[^/@\s]+@")]
    private static partial Regex UriUserInfo();
}
