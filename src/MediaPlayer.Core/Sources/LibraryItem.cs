namespace MediaPlayer.Core.Sources;

/// <summary>
/// One row of a media-server listing. Folders (libraries, series, seasons, box
/// sets, playlists) are navigated into; everything else is played.
/// </summary>
public sealed record LibraryItem(
    string Id,
    string Name,
    string Kind,
    string? Overview = null,
    string? Path = null,
    bool IsFolder = false,
    /// <summary>Library view type: movies, tvshows, music, boxsets, playlists …</summary>
    string? CollectionType = null,
    /// <summary>Episode number within the season, or season number for a season.</summary>
    int? IndexNumber = null,
    /// <summary>Season number for an episode.</summary>
    int? ParentIndexNumber = null,
    string? SeriesName = null,
    int? ProductionYear = null,
    /// <summary>Cache tag of the item's own poster / thumbnail; null when it has none.</summary>
    string? PrimaryImageTag = null,
    /// <summary>For episodes and seasons: the series whose poster stands in when the item has none.</summary>
    string? SeriesId = null,
    string? SeriesPrimaryImageTag = null,
    /// <summary>Resume progress 0–100 from the server's per-user data.</summary>
    double? PlayedPercentage = null,
    bool Played = false,
    /// <summary>Server-side resume point (100 ns ticks), for items last watched elsewhere.</summary>
    long? ResumePositionTicks = null,
    long? RunTimeTicks = null,
    /// <summary>Season of an episode; series of a season. Lets Next / auto-continue find neighbours.</summary>
    string? ParentId = null,
    string? SeasonId = null)
{
    public TimeSpan? ResumePosition => ResumePositionTicks is { } t && t > 0 ? TimeSpan.FromTicks(t) : null;

    public TimeSpan? RunTime => RunTimeTicks is { } t && t > 0 ? TimeSpan.FromTicks(t) : null;

    /// <summary>Time left from the resume point, when both are known.</summary>
    public TimeSpan? Remaining => ResumePosition is { } p && RunTime is { } r && r > p ? r - p : null;
    /// <summary>Views, episodes and folders carry 16:9 art; movies, series, seasons and sets carry 2:3 posters.</summary>
    public bool HasLandscapeArt => Kind is "Episode" or "CollectionFolder" or "UserView" or "Folder" or "Playlist" or "Video" or "Trailer";
    /// <summary>"S01E03" for an episode, "S01" for a season, otherwise null.</summary>
    public string? EpisodeCode => Kind switch
    {
        "Episode" when IndexNumber is { } e => ParentIndexNumber is { } s
            ? $"S{s:00}E{e:00}"
            : $"E{e:00}",
        "Season" when IndexNumber is { } s => $"S{s:00}",
        _ => null,
    };
}
