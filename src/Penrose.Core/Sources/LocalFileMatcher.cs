using Penrose.Core.Playback;

namespace Penrose.Core.Sources;

/// <summary>
/// IINA-style local subtitle matching: extra search folders, stem / episode
/// pairing, Levenshtein fallback, priority tokens. Same-directory names that
/// already contain the video stem are left to mpv <c>sub-auto=fuzzy</c>.
/// </summary>
public static class LocalFileMatcher
{
    public static readonly string[] SearchFolderNames = ["字幕", "Subs", "subs", "subtitles"];

    public static readonly string[] PriorityTokens =
        ["chs", "sc", "gb", "简体", "chi", "zh-cn", "zh", "chs&eng"];

    public static IReadOnlyList<string> EnumerateSubtitleFiles(string videoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        string full = Path.GetFullPath(videoPath);
        string? directory = Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        List<string> files = [];
        AddFromDirectory(directory, files);
        foreach (string name in SearchFolderNames)
        {
            string extra = Path.Combine(directory, name);
            if (Directory.Exists(extra))
            {
                AddFromDirectory(extra, files);
            }
        }

        return files;
    }

    /// <summary>
    /// Extra subtitle paths mpv fuzzy load from the video directory would miss.
    /// </summary>
    public static IReadOnlyList<string> MatchExtraSubtitles(string videoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        string full = Path.GetFullPath(videoPath);
        string videoName = Path.GetFileName(full);
        string videoDir = Path.GetDirectoryName(full) ?? "";
        List<MatchCandidate> candidates = [];
        foreach (string path in EnumerateSubtitleFiles(full))
        {
            candidates.Add(new MatchCandidate(Path.GetFileName(path), path));
        }

        IReadOnlyList<string> ranked = Rank(videoName, candidates);
        List<string> extra = [];
        foreach (string path in ranked)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            bool sameDir = string.Equals(dir, videoDir, StringComparison.OrdinalIgnoreCase);
            string subStem = Path.GetFileNameWithoutExtension(path);
            string videoStem = Path.GetFileNameWithoutExtension(videoName);
            if (sameDir && ContainsStem(subStem, videoStem))
            {
                continue;
            }

            extra.Add(path);
        }

        return extra;
    }

    public static IReadOnlyList<string> Rank(string videoFileName, IReadOnlyList<MatchCandidate> subtitles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoFileName);
        ArgumentNullException.ThrowIfNull(subtitles);
        string videoStem = Path.GetFileNameWithoutExtension(videoFileName);
        if (string.IsNullOrWhiteSpace(videoStem) || subtitles.Count == 0)
        {
            return [];
        }

        string? videoEpisode = EpisodeToken(videoStem);
        List<(MatchCandidate Sub, int Score, int Priority, int Distance)> scored = [];
        foreach (MatchCandidate sub in subtitles)
        {
            if (string.IsNullOrWhiteSpace(sub.FileName) || !LocalPlaybackFactory.IsSubtitlePath(sub.FileName))
            {
                continue;
            }

            string subStem = Path.GetFileNameWithoutExtension(sub.FileName);
            int score = 0;
            if (string.Equals(subStem, videoStem, StringComparison.OrdinalIgnoreCase))
            {
                score = 400;
            }
            else if (ContainsStem(subStem, videoStem))
            {
                score = 300;
            }
            else if (videoEpisode is not null
                && string.Equals(EpisodeToken(subStem), videoEpisode, StringComparison.OrdinalIgnoreCase))
            {
                score = 200;
            }
            else
            {
                int distance = Levenshtein(Normalize(videoStem), Normalize(subStem));
                int threshold = (int)Math.Ceiling((videoStem.Length + subStem.Length) * 0.3);
                if (distance <= threshold)
                {
                    score = 100 - Math.Min(distance, 99);
                }
            }

            if (score <= 0)
            {
                continue;
            }

            scored.Add((sub, score, PriorityHits(subStem), Levenshtein(Normalize(videoStem), Normalize(subStem))));
        }

        return scored
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Priority)
            .ThenBy(item => item.Distance)
            .ThenBy(item => item.Sub.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Sub.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static int Levenshtein(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];
        for (int j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    public static string? EpisodeToken(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(
                stem,
                @"S(\d{1,2})E(\d{1,3})|第\s*(\d{1,3})\s*[话集]|[\._ \-](\d{2,3})(?=[\._ \-]|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        if (match.Groups[1].Success)
        {
            return "s" + match.Groups[1].Value.PadLeft(2, '0')
                + "e" + match.Groups[2].Value.PadLeft(2, '0');
        }

        string number = match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
        if (number is "480" or "720" or "1080" or "2160")
        {
            return null;
        }

        return "e" + number.TrimStart('0').PadLeft(2, '0');
    }

    public static ExternalSubtitle ToExternalSubtitle(string path) =>
        new(new Uri(Path.GetFullPath(path)), Language: null, Title: Path.GetFileName(path), Encoding: null);

    public readonly record struct MatchCandidate(string FileName, string FullPath);

    private static void AddFromDirectory(string directory, List<string> files)
    {
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith('.') || !LocalPlaybackFactory.IsSubtitlePath(path))
            {
                continue;
            }

            files.Add(path);
        }
    }

    private static bool ContainsStem(string haystack, string stem) =>
        haystack.Contains(stem, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value)
    {
        char[] chars = value.ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] is '.' or '_' or '-' or '[' or ']' or '(' or ')')
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    private static int PriorityHits(string name)
    {
        int hits = 0;
        foreach (string token in PriorityTokens)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                hits++;
            }
        }

        return hits;
    }
}
