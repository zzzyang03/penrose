using System.Text.Json;

namespace Penrose.Core.Release;

public enum UpdateStatus
{
    InvalidVersion,
    UpToDate,
    Available,
}

public sealed record UpdateDecision(
    UpdateStatus Status,
    Version? Current,
    Version? Remote);

/// <summary>
/// Update check against GitHub Releases: the endpoint, tag parsing and the version
/// compare. Network I/O stays in the host.
/// </summary>
public static class AppRelease
{
    /// <summary>Latest release as JSON; its <c>tag_name</c> is the version tag (for example <c>v0.2.0</c>).</summary>
    public const string LatestReleaseApi = "https://api.github.com/repos/zzzyang03/penrose/releases/latest";

    /// <summary>Where the user downloads the release that <see cref="LatestReleaseApi"/> reported.</summary>
    public const string LatestReleasePage = "https://github.com/zzzyang03/penrose/releases/latest";

    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        int cut = trimmed.IndexOfAny(['+', '-', '/']);
        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        if (!Version.TryParse(trimmed, out Version? parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    public static int Compare(string? current, string? remote)
    {
        if (!TryParse(current, out Version left) || !TryParse(remote, out Version right))
        {
            return 0;
        }

        return left.CompareTo(right);
    }

    /// <summary>Reads <c>tag_name</c> from a GitHub release document.</summary>
    public static bool TryReadTag(string? json, out string tag)
    {
        tag = "";
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("tag_name", out JsonElement element)
                || element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string? value = element.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            tag = value.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static UpdateDecision CompareRemote(string? currentVersion, string? remoteVersion)
    {
        TryParse(currentVersion, out Version current);
        if (!TryParse(remoteVersion, out Version remote))
        {
            return new UpdateDecision(UpdateStatus.InvalidVersion, current, null);
        }

        return remote > current
            ? new UpdateDecision(UpdateStatus.Available, current, remote)
            : new UpdateDecision(UpdateStatus.UpToDate, current, remote);
    }
}
