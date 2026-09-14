namespace Penrose.Core.Sources;

/// <summary>
/// strm is untrusted: first valid line only, bounded length, protocol whitelist.
/// Never hand the file to a shell.
/// </summary>
public static class StrmParser
{
    public const int MaxBytes = 4096;

    private static readonly string[] AllowedSchemes = ["http", "https", "file"];

    public static Uri? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxBytes)
        {
            return null;
        }

        foreach (string raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith(@"\\", StringComparison.Ordinal))
            {
                try
                {
                    return new Uri(line);
                }
                catch (UriFormatException)
                {
                    return null;
                }
            }

            if (!Uri.TryCreate(line, UriKind.Absolute, out Uri? uri))
            {
                return null;
            }

            if (AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
            {
                return uri;
            }

            return null;
        }

        return null;
    }
}
