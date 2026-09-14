namespace MediaPlayer.Core.Release;

public enum UpdateStatus
{
    Disabled,
    InvalidFeed,
    ReadyToQuery,
    UpToDate,
    Available,
}

public sealed record UpdateDecision(
    UpdateStatus Status,
    Version? Current,
    Version? Remote);

/// <summary>
/// Version compare for portable zip / Velopack. Network I/O stays in the host.
/// </summary>
public static class AppRelease
{
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

    public static UpdateDecision ForFeed(string? currentVersion, string? feedUrl)
    {
        TryParse(currentVersion, out Version current);
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return new UpdateDecision(UpdateStatus.Disabled, current, null);
        }

        if (!Uri.TryCreate(feedUrl.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new UpdateDecision(UpdateStatus.InvalidFeed, current, null);
        }

        return new UpdateDecision(UpdateStatus.ReadyToQuery, current, null);
    }

    public static UpdateDecision CompareRemote(string? currentVersion, string? remoteVersion)
    {
        TryParse(currentVersion, out Version current);
        if (!TryParse(remoteVersion, out Version remote))
        {
            return new UpdateDecision(UpdateStatus.InvalidFeed, current, null);
        }

        return remote > current
            ? new UpdateDecision(UpdateStatus.Available, current, remote)
            : new UpdateDecision(UpdateStatus.UpToDate, current, remote);
    }
}
