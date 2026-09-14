using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MediaPlayer.Core.Playback;
using MediaPlayer.Core.Sources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Serilog;

namespace MediaPlayer.App.WinUI;

/// <summary>
/// The item page of the library (movie, episode, series, season, box set):
/// backdrop hero, poster and facts, server flags, play / resume, media cards,
/// then the children, cast, overview, art, similar titles and external links —
/// the same blocks Emby's own item page shows, from the same endpoints.
/// </summary>
public sealed partial class MainWindow
{
    private LibraryItemDetails? _details;
    private double _childTileWidth = PosterBaseWidth;
    private double _childImageHeight = 225;
    private bool _childLandscape;
    /// <summary>Version / audio / subtitle picked in the media cards for the next play.</summary>
    private PlaybackSelection _selection = PlaybackSelection.Default;

    private void WireItemPage()
    {
        DetailsPlayButton.Click += async (_, _) => await PlayFromDetailsAsync(fromStart: false).ConfigureAwait(true);
        DetailsRestartButton.Click += async (_, _) => await PlayFromDetailsAsync(fromStart: true).ConfigureAwait(true);
        DetailsPlayedToggle.Click += async (_, _) => await ToggleFlagAsync(played: true).ConfigureAwait(true);
        DetailsFavoriteToggle.Click += async (_, _) => await ToggleFlagAsync(played: false).ConfigureAwait(true);
        DetailsChildren.ItemClick += async (s, e) => await OnPosterClickAsync(s, e).ConfigureAwait(true);
        NextUpList.ItemClick += async (s, e) => await OnPosterClickAsync(s, e).ConfigureAwait(true);
        SimilarList.ItemClick += async (s, e) => await OnPosterClickAsync(s, e).ConfigureAwait(true);
        DetailsOverviewMore.Click += (_, _) => ToggleOverview();
        DetailsOverview.IsTextTrimmedChanged += (_, _) => UpdateOverviewToggle();
        WireStrip(NextUpList, NextUpPrev, NextUpNext);
        WireStrip(PeopleList, PeoplePrev, PeopleNext, buttonsOnly: true);
        WireStrip(ArtList, ArtPrev, ArtNext);
        WireStrip(SimilarList, SimilarPrev, SimilarNext, buttonsOnly: true);
        DetailsChildren.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5 && DetailsChildren.ItemsSource is not null)
            {
                (_childTileWidth, _childImageHeight) = FitWall(DetailsChildren, e.NewSize.Width, _childLandscape);
            }
        };
        // The breadcrumb row floats over the hero; once content scrolls under it, it gets a solid backing.
        DetailsScroll.ViewChanged += (_, _) => UpdateHeaderBackdrop();
    }

    private void UpdateHeaderBackdrop() =>
        HeaderBackdrop.Opacity = DetailsScroll.Visibility == Visibility.Visible
            ? Math.Clamp(DetailsScroll.VerticalOffset / 160, 0, 1)
            : 0;

    private void ApplyItemPageLanguage()
    {
        NextUpHeader.Text = _ui.SectionNextUp;
        PeopleHeader.Text = _ui.SectionPeople;
        ArtHeader.Text = _ui.SectionArt;
        SimilarHeader.Text = _ui.SectionSimilar;
        LinksHeader.Text = _ui.SectionLinks;
        DetailsRestartText.Text = _ui.PlayFromStart;
        UpdateOverviewToggle();
        foreach ((Button prev, Button next) in new[] { (NextUpPrev, NextUpNext), (PeoplePrev, PeopleNext), (ArtPrev, ArtNext), (SimilarPrev, SimilarNext) })
        {
            SetName(prev, _ui.Previous);
            SetName(next, _ui.Next);
        }

        SetName(VideoCard, _ui.VersionLabel);
        SetName(AudioCard, _ui.AudioTrackLabel);
        SetName(SubtitleCard, _ui.SubtitleTrackLabel);
        SetName(DetailsChildren, _ui.SectionContents);
        SetName(NextUpList, _ui.SectionNextUp);
        SetName(SimilarList, _ui.SectionSimilar);
        SetName(PeopleList, _ui.SectionPeople);
        SetName(DetailsRestartButton, _ui.PlayFromStart);
        SetName(DetailsBackdrop, _ui.SectionArt);
        if (_details is { } details)
        {
            RefreshFlagChips(details.Played, details.IsFavorite);
        }
        else
        {
            RefreshFlagChips(false, false);
        }
    }

    /// <summary>
    /// Builds the page for <paramref name="item"/>: what the listing row already
    /// knows shows at once, the full record, children, next-up and similar titles
    /// fill in as they arrive. <paramref name="ticket"/> drops stale results.
    /// </summary>
    private async Task ShowItemPageAsync(IContentProvider provider, LibraryItem item, int ticket)
    {
        _details = null;
        PosterGrid.Visibility = Visibility.Collapsed;
        LibraryMessagePanel.Visibility = Visibility.Collapsed;
        LibraryCount.Text = "";
        ResetItemPage();
        FillHeader(provider, item, details: null);
        DetailsScroll.Visibility = Visibility.Visible;
        DetailsScroll.ChangeView(null, 0, null, disableAnimation: true);

        Task<LibraryItemDetails?> detailsTask = provider.GetDetailsAsync(item.Id);
        Task<LibraryPage>? childrenTask = item.IsFolder ? provider.BrowsePageAsync(item.Id, 0, item, sort: null) : null;
        Task<IReadOnlyList<LibraryItem>>? nextUpTask = item.Kind == "Series" ? provider.NextUpAsync(item.Id, 6) : null;
        Task<IReadOnlyList<LibraryItem>> similarTask = provider.SimilarAsync(item.Id, 12);

        try
        {
            LibraryItemDetails? details = await detailsTask.ConfigureAwait(true);
            if (ticket != _libraryTicket)
            {
                return;
            }

            if (details is not null)
            {
                _details = details;
                FillHeader(provider, details.Item, details);
                FillPeople(provider, details);
                FillArt(provider, details);
                FillLinks(details);
            }
        }
        catch (Exception ex)
        {
            if (ticket != _libraryTicket)
            {
                return;
            }

            Log.Warning(ex, "Item details failed");
            ShowError(DescribeEmbyFailure(ex));
        }
        finally
        {
            if (ticket == _libraryTicket)
            {
                LibraryBusyRing.IsActive = false;
                LibraryBusy.Visibility = Visibility.Collapsed;
            }
        }

        if (childrenTask is not null)
        {
            await FillChildrenAsync(provider, item, childrenTask, ticket).ConfigureAwait(true);
        }

        if (nextUpTask is not null)
        {
            await FillStripAsync(nextUpTask, NextUpSection, NextUpList, provider, ticket, landscape: true, withSeries: false).ConfigureAwait(true);
        }

        await FillStripAsync(similarTask, SimilarSection, SimilarList, provider, ticket, landscape: false, withSeries: true).ConfigureAwait(true);
        if (ticket == _libraryTicket)
        {
            UpdatePlayButton(item);
        }
    }

    private void ResetItemPage()
    {
        DetailsBackdrop.Source = null;
        DetailsPoster.Source = null;
        DetailsGenres.Children.Clear();
        DetailsLinks.Children.Clear();
        DetailsTagline.Visibility = Visibility.Collapsed;
        DetailsOverview.Text = "";
        DetailsOverview.MaxLines = 3;
        DetailsOverviewMore.Visibility = Visibility.Collapsed;
        DetailsExtraFacts.Visibility = Visibility.Collapsed;
        DetailsRatingBox.Visibility = Visibility.Collapsed;
        DetailsOfficialBox.Visibility = Visibility.Collapsed;
        DetailsMediaCards.Visibility = Visibility.Collapsed;
        DetailsPlayRow.Visibility = Visibility.Collapsed;
        DetailsPlaySub.Visibility = Visibility.Collapsed;
        DetailsRestartButton.Visibility = Visibility.Collapsed;
        DetailsPlayButton.Tag = null;
        _selection = PlaybackSelection.Default;
        foreach (StackPanel section in new[] { NextUpSection, ChildrenSection, PeopleSection, ArtSection, SimilarSection, LinksSection })
        {
            section.Visibility = Visibility.Collapsed;
        }

        NextUpList.ItemsSource = null;
        DetailsChildren.ItemsSource = null;
        PeopleList.ItemsSource = null;
        ArtList.ItemsSource = null;
        SimilarList.ItemsSource = null;
    }

    // ---- hero ------------------------------------------------------------------

    private void FillHeader(IContentProvider provider, LibraryItem item, LibraryItemDetails? details)
    {
        DetailsTitle.Text = item.Name;
        bool portrait = !item.HasLandscapeArt;
        DetailsPosterBox.Width = portrait ? 200 : 320;
        DetailsPosterBox.Height = portrait ? 300 : 180;
        if (provider.ImageUrl(item, portrait ? 400 : 640, portrait ? 600 : 360) is { } poster)
        {
            DetailsPoster.Source = PosterImageCache.Get(poster, decodeWidth: portrait ? 400 : 640);
        }

        if (details is not null && provider.BackdropUrl(details, 1920) is { } backdrop)
        {
            DetailsBackdrop.Source = PosterImageCache.Get(backdrop, decodeWidth: 1920);
        }

        // ★ 8.3 · [TV-MA] · Black Mirror · 2011–2019 · 44 分钟 · 已完结
        if (details?.CommunityRating is { } rating)
        {
            DetailsRating.Text = rating.ToString("0.0", CultureInfo.InvariantCulture);
            DetailsRatingBox.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(details?.OfficialRating))
        {
            DetailsOfficialRating.Text = details.OfficialRating;
            DetailsOfficialBox.Visibility = Visibility.Visible;
        }

        DetailsMeta.Text = MetaLine(item, details);
        DetailsGenres.Children.Clear();
        foreach (string genre in details?.Genres.Take(5) ?? [])
        {
            DetailsGenres.Children.Add(new Border
            {
                Background = Brush("HoverFillBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 3, 9, 3),
                Child = new TextBlock { Text = genre, FontSize = 12.5, Foreground = Brush("TextSecondaryBrush") },
            });
        }

        DetailsTagline.Text = details?.Tagline ?? "";
        DetailsTagline.Visibility = string.IsNullOrWhiteSpace(details?.Tagline) ? Visibility.Collapsed : Visibility.Visible;
        DetailsOverview.Text = details?.Overview ?? item.Overview ?? "";
        UpdateOverviewToggle();
        FillFacts(details);
        DetailsActions.Visibility = Visibility.Visible;
        RefreshFlagChips(details?.Played ?? item.Played, details?.IsFavorite ?? false);
        FillMediaCards(details);
        UpdatePlayButton(item);
    }

    /// <summary>首播 / 出品 under the overview.</summary>
    private void FillFacts(LibraryItemDetails? details)
    {
        List<string> facts = [];
        if (details?.PremiereDate is { } premiere)
        {
            facts.Add(_ui.PremiereLabel + "：" + premiere.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (details?.Studios.Count > 0)
        {
            facts.Add(_ui.StudiosLabel + "：" + string.Join(", ", details.Studios.Take(4)));
        }

        DetailsExtraFacts.Text = string.Join("    ", facts);
        DetailsExtraFacts.Visibility = facts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The overview shows three lines; "显示全部" opens it, "收起" folds it back.</summary>
    private void ToggleOverview()
    {
        DetailsOverview.MaxLines = DetailsOverview.MaxLines == 0 ? 3 : 0;
        UpdateOverviewToggle();
    }

    private void UpdateOverviewToggle()
    {
        bool expanded = DetailsOverview.MaxLines == 0;
        DetailsOverviewMore.Content = expanded ? _ui.ShowLess : _ui.ShowAll;
        DetailsOverviewMore.Visibility = DetailsOverview.Text.Length > 0 && (expanded || DetailsOverview.IsTextTrimmed)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string MetaLine(LibraryItem item, LibraryItemDetails? details)
    {
        List<string> parts = [];
        if (item.Kind == "Episode")
        {
            if (!string.IsNullOrEmpty(item.SeriesName))
            {
                parts.Add(item.SeriesName);
            }

            if (item.EpisodeCode is { } code)
            {
                parts.Add(code);
            }
        }
        else if (details?.OriginalTitle is { Length: > 0 } original && !string.Equals(original, item.Name, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(original);
        }

        int? start = details?.PremiereDate?.Year ?? item.ProductionYear;
        if (item.Kind == "Series" && start is { } from)
        {
            int? end = details?.EndDate?.Year;
            bool ended = string.Equals(details?.Status, "Ended", StringComparison.OrdinalIgnoreCase);
            parts.Add(end is { } to && to != from ? $"{from}–{to}" : ended ? from.ToString(CultureInfo.InvariantCulture) : from + "–");
        }
        else if (start is { } year)
        {
            parts.Add(year.ToString(CultureInfo.InvariantCulture));
        }

        if (item.RunTime is { } runtime)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, _ui.MinutesShort, Math.Max(1, (int)Math.Round(runtime.TotalMinutes))));
        }

        if (item.Kind == "Series" && details?.Status is { Length: > 0 } status)
        {
            parts.Add(string.Equals(status, "Ended", StringComparison.OrdinalIgnoreCase) ? _ui.StatusEnded : _ui.StatusContinuing);
        }

        return string.Join("  ·  ", parts);
    }

    private void RefreshFlagChips(bool played, bool favorite)
    {
        DetailsPlayedToggle.IsChecked = played;
        DetailsPlayedText.Text = played ? _ui.MarkedPlayed : _ui.MarkPlayed;
        DetailsFavoriteToggle.IsChecked = favorite;
        DetailsFavoriteText.Text = favorite ? _ui.Favorited : _ui.Favorite;
        DetailsFavoriteGlyph.Glyph = favorite ? "\uEB52" : "\uEB51";
        SetName(DetailsPlayedToggle, DetailsPlayedText.Text);
        SetName(DetailsFavoriteToggle, DetailsFavoriteText.Text);
    }

    /// <summary>Played / favourite flags live on the server; the chip reflects the reply.</summary>
    private async Task ToggleFlagAsync(bool played)
    {
        if (_embyLibrary is not { } provider || _libraryFolder is not { } item)
        {
            return;
        }

        ToggleButton chip = played ? DetailsPlayedToggle : DetailsFavoriteToggle;
        bool on = chip.IsChecked == true;
        try
        {
            if (played)
            {
                await provider.SetPlayedAsync(item.Id, on).ConfigureAwait(true);
            }
            else
            {
                await provider.SetFavoriteAsync(item.Id, on).ConfigureAwait(true);
            }

            if (_details is { } details && details.Item.Id == item.Id)
            {
                _details = played ? details with { Played = on } : details with { IsFavorite = on };
            }

            RefreshFlagChips(DetailsPlayedToggle.IsChecked == true, DetailsFavoriteToggle.IsChecked == true);
            Log.Information("Library: {Flag} {State} for {Item}", played ? "played" : "favourite", on ? "set" : "cleared", item.Name);
        }
        catch (Exception ex)
        {
            chip.IsChecked = !on;
            RefreshFlagChips(DetailsPlayedToggle.IsChecked == true, DetailsFavoriteToggle.IsChecked == true);
            Log.Warning(ex, "Flag update failed");
            ShowError(DescribeEmbyFailure(ex));
        }
    }

    // ---- play ------------------------------------------------------------------

    /// <summary>
    /// What the big button plays: the item itself when playable, otherwise the
    /// first next-up episode (series) or the first unplayed child (season, set).
    /// </summary>
    private void UpdatePlayButton(LibraryItem page)
    {
        LibraryItem? target = page.IsFolder ? FirstPlayable() : page;
        if (target is null)
        {
            DetailsPlayRow.Visibility = Visibility.Collapsed;
            DetailsPlayButton.Tag = null;
            return;
        }

        bool resumable = target.PlayedPercentage is > 0 and < 100 && target.ResumePosition is not null;
        DetailsPlayText.Text = resumable
            ? (target.Remaining is { } left
                ? string.Format(CultureInfo.InvariantCulture, _ui.ContinueWatchingRemaining, Math.Max(1, (int)Math.Round(left.TotalMinutes)))
                : _ui.ContinueWatching)
            : page.IsFolder ? _ui.StartPlaying : _ui.Play;
        string sub = page.IsFolder ? (target.EpisodeCode is { } code ? code + "  " + target.Name : target.Name) : "";
        DetailsPlaySub.Text = sub;
        DetailsPlaySub.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailsRestartButton.Visibility = resumable ? Visibility.Visible : Visibility.Collapsed;
        DetailsPlayButton.Tag = target;
        DetailsPlayRow.Visibility = Visibility.Visible;
        SetName(DetailsPlayButton, DetailsPlayText.Text + (sub.Length > 0 ? " " + sub : ""));
    }

    private LibraryItem? FirstPlayable()
    {
        if (NextUpList.ItemsSource is IEnumerable<PosterRow> next && next.FirstOrDefault() is { } nextUp)
        {
            return nextUp.Item;
        }

        if (DetailsChildren.ItemsSource is IEnumerable<PosterRow> children)
        {
            List<LibraryItem> playable = children.Select(r => r.Item).Where(i => !i.IsFolder).ToList();
            return playable.FirstOrDefault(i => i.Played != true && i.PlayedPercentage is null or < 100) ?? playable.FirstOrDefault();
        }

        return null;
    }


    private async Task PlayFromDetailsAsync(bool fromStart)
    {
        if (DetailsPlayButton.Tag is not LibraryItem item)
        {
            return;
        }

        List<LibraryItem> queue = (DetailsChildren.ItemsSource as IEnumerable<PosterRow> ?? [])
            .Select(r => r.Item)
            .Where(i => !i.IsFolder)
            .ToList();
        // The cards describe this page's item; a child (next-up episode) plays with defaults.
        PlaybackSelection? selection = _details is { } details && details.Item.Id == item.Id ? _selection : null;
        await PlayLibraryItemAsync(item, null, queue.Count > 0 ? queue : _libraryQueue, fromStart, selection).ConfigureAwait(true);
    }

    /// <summary>
    /// Maps the page's stream picks onto mpv track numbers for a direct play of
    /// <paramref name="sourceId"/>: mpv numbers tracks per type in container order,
    /// which is the order Emby lists MediaStreams in.
    /// </summary>
    private (int? Aid, int? Sid) TrackNumbersFor(PlaybackSelection selection, string? sourceId)
    {
        MediaSourceInfo? source = _details?.Sources.FirstOrDefault(s => s.Id == (sourceId ?? selection.MediaSourceId))
            ?? _details?.Sources.FirstOrDefault();
        if (source is null)
        {
            return (null, null);
        }

        int? aid = null;
        if (selection.AudioStreamIndex is { } audioIndex)
        {
            int position = 0;
            foreach (MediaStreamInfo stream in source.Streams.Where(s => s.Type == "Audio"))
            {
                position++;
                if (stream.Index == audioIndex)
                {
                    aid = position;
                    break;
                }
            }
        }

        int? sid = null;
        if (selection.SubtitleStreamIndex is { } subtitleIndex)
        {
            if (subtitleIndex < 0)
            {
                sid = 0; // "无字幕"
            }
            else
            {
                // External subtitles are added after the load and get later numbers; they keep mpv's default choice.
                int position = 0;
                foreach (MediaStreamInfo stream in source.Streams.Where(s => s.Type == "Subtitle" && !s.IsExternal))
                {
                    position++;
                    if (stream.Index == subtitleIndex)
                    {
                        sid = position;
                        break;
                    }
                }
            }
        }

        return (aid, sid);
    }

    // ---- media cards -----------------------------------------------------------

    /// <summary>
    /// Version / audio / subtitle cards: each is a drop-down over the server's
    /// MediaSources and MediaStreams; the card face shows the current pick.
    /// </summary>
    private void FillMediaCards(LibraryItemDetails? details)
    {
        if (details is null || details.Item.IsFolder || details.Sources.Count == 0)
        {
            DetailsMediaCards.Visibility = Visibility.Collapsed;
            return;
        }

        MediaSourceInfo source = details.Sources.FirstOrDefault(s => s.Id == _selection.MediaSourceId) ?? details.Sources[0];
        MediaStreamInfo? audio = PickedStream(source, "Audio", _selection.AudioStreamIndex);
        MediaStreamInfo? subtitle = _selection.SubtitleStreamIndex is < 0 ? null : PickedStream(source, "Subtitle", _selection.SubtitleStreamIndex);

        // Video card: the version.
        DetailsVideoTitle.Text = VersionLabel(source);
        DetailsVideoDetail.Text = VersionDetail(source);
        VideoFlyout.Items.Clear();
        foreach (MediaSourceInfo version in details.Sources)
        {
            RadioMenuFlyoutItem item = new() { Text = VersionLabel(version), GroupName = "version", IsChecked = version.Id == source.Id };
            string id = version.Id;
            item.Click += (_, _) =>
            {
                // A different file has different tracks: the picks start over.
                _selection = new PlaybackSelection(id, null, null);
                FillMediaCards(_details);
            };
            VideoFlyout.Items.Add(item);
        }

        // Audio card.
        List<MediaStreamInfo> audios = source.Streams.Where(s => s.Type == "Audio").ToList();
        DetailsAudioTitle.Text = audio is null ? _ui.DetailsNone : StreamLabel(audio);
        DetailsAudioDetail.Text = audio is null ? "" : AudioDetail(audio, audios.Count);
        AudioFlyout.Items.Clear();
        foreach (MediaStreamInfo stream in audios)
        {
            RadioMenuFlyoutItem item = new() { Text = StreamLabel(stream) + "  ·  " + AudioDetail(stream, 1), GroupName = "audio", IsChecked = audio is not null && stream.Index == audio.Index };
            int index = stream.Index;
            item.Click += (_, _) =>
            {
                _selection = _selection with { MediaSourceId = source.Id, AudioStreamIndex = index };
                FillMediaCards(_details);
            };
            AudioFlyout.Items.Add(item);
        }

        // Subtitle card, with "无字幕" as the first choice.
        List<MediaStreamInfo> subtitles = source.Streams.Where(s => s.Type == "Subtitle").ToList();
        DetailsSubtitleTitle.Text = subtitle is null ? _ui.NoSubtitles : StreamLabel(subtitle);
        DetailsSubtitleDetail.Text = subtitles.Count == 0
            ? ""
            : subtitle is null
                ? string.Format(CultureInfo.InvariantCulture, _ui.SubtitleCount, subtitles.Count)
                : string.Join(" / ", new[] { subtitle.Codec?.ToUpperInvariant(), subtitle.IsExternal ? _ui.ExternalTrack : null, string.Format(CultureInfo.InvariantCulture, _ui.SubtitleCount, subtitles.Count) }.Where(s => !string.IsNullOrEmpty(s)));
        SubtitleFlyout.Items.Clear();
        RadioMenuFlyoutItem none = new() { Text = _ui.NoSubtitles, GroupName = "subtitle", IsChecked = subtitle is null };
        none.Click += (_, _) =>
        {
            _selection = _selection with { MediaSourceId = source.Id, SubtitleStreamIndex = -1 };
            FillMediaCards(_details);
        };
        SubtitleFlyout.Items.Add(none);
        foreach (MediaStreamInfo stream in subtitles)
        {
            string text = StreamLabel(stream) + "  ·  " + (stream.Codec?.ToUpperInvariant() ?? "") + (stream.IsExternal ? "  ·  " + _ui.ExternalTrack : "");
            RadioMenuFlyoutItem item = new() { Text = text, GroupName = "subtitle", IsChecked = subtitle is not null && stream.Index == subtitle.Index };
            int index = stream.Index;
            item.Click += (_, _) =>
            {
                _selection = _selection with { MediaSourceId = source.Id, SubtitleStreamIndex = index };
                FillMediaCards(_details);
            };
            SubtitleFlyout.Items.Add(item);
        }

        AudioCard.IsEnabled = audios.Count > 0;
        SubtitleCard.IsEnabled = subtitles.Count > 0;
        VideoCard.IsEnabled = details.Sources.Count > 1;
        DetailsMediaCards.Visibility = Visibility.Visible;
    }

    /// <summary>The picked stream of a type, else the server's default, else the first.</summary>
    private static MediaStreamInfo? PickedStream(MediaSourceInfo source, string type, int? index)
    {
        List<MediaStreamInfo> streams = source.Streams.Where(s => s.Type == type).ToList();
        return (index is { } i ? streams.FirstOrDefault(s => s.Index == i) : null)
            ?? streams.FirstOrDefault(s => s.IsDefault)
            ?? streams.FirstOrDefault();
    }

    private static string VersionLabel(MediaSourceInfo source)
    {
        MediaStreamInfo? video = source.Streams.FirstOrDefault(s => s.Type == "Video");
        // Emby labels by width class, not the exact (often cropped) height.
        string resolution = video?.Width switch
        {
            >= 3800 => "4K",
            >= 1900 => "1080p",
            >= 1260 => "720p",
            >= 700 => "480p",
            > 0 => "SD",
            _ => "",
        };
        return resolution.Length > 0 && !source.Name.Contains(resolution, StringComparison.OrdinalIgnoreCase)
            ? source.Name + " - " + resolution
            : source.Name;
    }

    private static string VersionDetail(MediaSourceInfo source)
    {
        MediaStreamInfo? video = source.Streams.FirstOrDefault(s => s.Type == "Video");
        List<string> v = [];
        if (video is not null)
        {
            if (!string.IsNullOrEmpty(video.Codec))
            {
                v.Add(video.Codec.ToUpperInvariant());
            }

            if (!string.IsNullOrEmpty(video.VideoRange) && video.VideoRange != "SDR")
            {
                v.Add(video.VideoRange);
            }

            if (video.FrameRate is { } fps)
            {
                v.Add(fps.ToString("0.###", CultureInfo.InvariantCulture) + " fps");
            }
        }

        if (source.Bitrate is { } bitrate)
        {
            v.Add((bitrate / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + " Mbps");
        }

        if (source.Size is { } size)
        {
            v.Add(FormatSize(size));
        }

        if (!string.IsNullOrEmpty(source.Container))
        {
            v.Add(source.Container.ToUpperInvariant());
        }

        return string.Join(" / ", v);
    }

    private static string StreamLabel(MediaStreamInfo stream) =>
        stream.Title ?? string.Join(" ", new[] { stream.Language, stream.Codec?.ToUpperInvariant() }.Where(s => !string.IsNullOrEmpty(s)));

    private static string AudioDetail(MediaStreamInfo audio, int count)
    {
        List<string> a = [];
        if (ChannelLayouts.Label(audio.ChannelLayout, audio.Channels) is { } layout)
        {
            a.Add(layout);
        }

        if (!string.IsNullOrEmpty(audio.Codec))
        {
            a.Add(audio.Codec.ToUpperInvariant());
        }

        if (audio.BitRate is { } abr)
        {
            a.Add((abr / 1000).ToString(CultureInfo.InvariantCulture) + " kbps");
        }

        if (count > 1)
        {
            a.Add(count + " ×");
        }

        return string.Join(" / ", a);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.#", CultureInfo.InvariantCulture) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB",
    };

    // ---- sections --------------------------------------------------------------

    private async Task FillChildrenAsync(IContentProvider provider, LibraryItem parent, Task<LibraryPage> task, int ticket)
    {
        try
        {
            LibraryPage page = await task.ConfigureAwait(true);
            if (ticket != _libraryTicket)
            {
                return;
            }

            ChildrenHeader.Text = parent.Kind switch
            {
                "Series" => _ui.SectionSeasons,
                "Season" => _ui.SectionEpisodes,
                _ => _ui.SectionContents,
            };
            if (page.Items.Count == 0)
            {
                ChildrenSection.Visibility = Visibility.Collapsed;
                return;
            }

            _childLandscape = page.Items.All(i => i.HasLandscapeArt);
            double width = EmbyPage.ActualWidth - DetailsSections.Margin.Left - DetailsSections.Margin.Right;
            (_childTileWidth, _childImageHeight) = FitWall(DetailsChildren, width, _childLandscape);
            DetailsChildren.ItemsSource = page.Items.Select(i => PosterFor(provider, i, _childTileWidth, _childImageHeight, withSeries: false)).ToList();
            ChildrenSection.Visibility = Visibility.Visible;
            LibraryCount.Text = string.Format(
                CultureInfo.CurrentCulture,
                _ui.LibraryItemCount,
                (page.TotalCount ?? page.Items.Count).ToString("N0", CultureInfo.CurrentCulture));
            DetailsChildren.UpdateLayout();
            (_childTileWidth, _childImageHeight) = FitWall(DetailsChildren, DetailsChildren.ActualWidth > 0 ? DetailsChildren.ActualWidth : width, _childLandscape);
        }
        catch (Exception ex)
        {
            if (ticket == _libraryTicket)
            {
                Log.Warning(ex, "Item children failed");
                ShowError(DescribeEmbyFailure(ex));
            }
        }
    }

    private async Task FillStripAsync(
        Task<IReadOnlyList<LibraryItem>> task,
        StackPanel section,
        GridView list,
        IContentProvider provider,
        int ticket,
        bool landscape,
        bool withSeries)
    {
        try
        {
            IReadOnlyList<LibraryItem> items = await task.ConfigureAwait(true);
            if (ticket != _libraryTicket)
            {
                return;
            }

            if (items.Count == 0)
            {
                section.Visibility = Visibility.Collapsed;
                return;
            }

            double width = landscape ? ThumbBaseWidth : PosterBaseWidth;
            double height = landscape ? Math.Round(ThumbBaseWidth * 9 / 16) : 225;
            list.ItemsSource = items.Select(i => PosterFor(provider, i, width, height, withSeries)).ToList();
            section.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            if (ticket == _libraryTicket)
            {
                Log.Warning(ex, "Item strip {Section} failed", section.Name);
                section.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void FillPeople(IContentProvider provider, LibraryItemDetails details)
    {
        if (details.People.Count == 0)
        {
            PeopleSection.Visibility = Visibility.Collapsed;
            return;
        }

        // Actors first (as billed), then crew, as Emby orders its strip.
        IEnumerable<PersonInfo> ordered = details.People
            .Where(p => p.Type is "Actor" or "GuestStar")
            .Concat(details.People.Where(p => p.Type is not ("Actor" or "GuestStar")))
            .Take(30);
        PeopleList.ItemsSource = ordered.Select(p => new PersonRow(p, RoleLabel(p), provider.PersonImageUrl(p, 332))).ToList();
        PeopleSection.Visibility = Visibility.Visible;
    }

    private string RoleLabel(PersonInfo person) => person.Type switch
    {
        "Actor" => string.IsNullOrWhiteSpace(person.Role) ? _ui.RoleActor : person.Role,
        "GuestStar" => string.IsNullOrWhiteSpace(person.Role) ? _ui.RoleGuestStar : person.Role,
        "Director" => _ui.RoleDirector,
        "Writer" => _ui.RoleWriter,
        "Producer" => _ui.RoleProducer,
        "Composer" => _ui.RoleComposer,
        _ => person.Role ?? person.Type,
    };

    // ---- strips ----------------------------------------------------------------

    /// <summary>Chrome for a horizontal strip: the ‹ › pair, and whether the wheel must not slide it.</summary>
    private sealed record StripChrome(Button Prev, Button Next, bool ButtonsOnly);

    /// <summary>
    /// ‹ › page a strip by most of its width; the buttons grey out at either end.
    /// <paramref name="buttonsOnly"/> keeps the wheel for the page (演职人员 / 更多类似):
    /// the strip itself only moves when a button calls <see cref="PageStrip"/>.
    /// </summary>
    private void WireStrip(GridView list, Button prev, Button next, bool buttonsOnly = false)
    {
        prev.Click += (_, _) => PageStrip(list, -1);
        next.Click += (_, _) => PageStrip(list, +1);
        if (buttonsOnly)
        {
            // The strip's ScrollViewer would otherwise turn the wheel into a
            // sideways slide; take the event (even after it does) and give the
            // delta to the page instead.
            list.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnButtonOnlyStripWheel), handledEventsToo: true);
        }

        list.Loaded += (_, _) =>
        {
            if (FindScrollViewer(list) is { } viewer)
            {
                viewer.ViewChanged -= OnStripViewChanged;
                viewer.ViewChanged += OnStripViewChanged;
                viewer.Tag = new StripChrome(prev, next, buttonsOnly);
                if (buttonsOnly)
                {
                    viewer.HorizontalScrollMode = ScrollMode.Disabled;
                }

                UpdateStripArrows(viewer, prev, next);
            }
        };
        list.SizeChanged += (_, _) =>
        {
            if (FindScrollViewer(list) is { } viewer)
            {
                UpdateStripArrows(viewer, prev, next);
            }
        };
    }

    /// <summary>Wheel over 演职人员 / 更多类似 scrolls the item page, not the strip.</summary>
    private void OnButtonOnlyStripWheel(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        if (delta != 0)
        {
            DetailsScroll.ChangeView(null, DetailsScroll.VerticalOffset - delta, null);
        }

        e.Handled = true;
    }

    private static void OnStripViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer { Tag: StripChrome chrome } viewer)
        {
            UpdateStripArrows(viewer, chrome.Prev, chrome.Next);
        }
    }

    private static void PageStrip(GridView list, int direction)
    {
        if (FindScrollViewer(list) is not { } viewer)
        {
            return;
        }

        bool buttonsOnly = viewer.Tag is StripChrome { ButtonsOnly: true };
        if (buttonsOnly)
        {
            // ChangeView is ignored while the mode is Disabled (that lock is what
            // keeps the wheel from sliding the strip).
            viewer.HorizontalScrollMode = ScrollMode.Enabled;
        }

        double step = Math.Max(120, viewer.ViewportWidth * 0.85);
        bool? moved = viewer.ChangeView(
            Math.Clamp(viewer.HorizontalOffset + direction * step, 0, Math.Max(0, viewer.ScrollableWidth)),
            null,
            null);
        if (!buttonsOnly)
        {
            return;
        }

        if (moved != true)
        {
            viewer.HorizontalScrollMode = ScrollMode.Disabled;
            return;
        }

        void Restore(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate)
            {
                return;
            }

            viewer.ViewChanged -= Restore;
            viewer.HorizontalScrollMode = ScrollMode.Disabled;
        }

        viewer.ViewChanged += Restore;
    }

    private static void UpdateStripArrows(ScrollViewer viewer, Button prev, Button next)
    {
        bool scrollable = viewer.ScrollableWidth > 1;
        prev.IsEnabled = scrollable && viewer.HorizontalOffset > 1;
        next.IsEnabled = scrollable && viewer.HorizontalOffset < viewer.ScrollableWidth - 1;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private void FillArt(IContentProvider provider, LibraryItemDetails details)
    {
        // One backdrop is already the hero; a strip only earns its place with more.
        if (details.BackdropTags.Count < 2)
        {
            ArtSection.Visibility = Visibility.Collapsed;
            return;
        }

        List<ArtRow> rows = [];
        for (int i = 0; i < Math.Min(details.BackdropTags.Count, 12); i++)
        {
            if (provider.BackdropUrl(details, i, 464) is { } url)
            {
                rows.Add(new ArtRow(url));
            }
        }

        ArtList.ItemsSource = rows;
        ArtSection.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FillLinks(LibraryItemDetails details)
    {
        DetailsLinks.Children.Clear();
        foreach (ExternalLink link in details.ExternalUrls.Take(8))
        {
            DetailsLinks.Children.Add(new HyperlinkButton
            {
                Content = link.Name,
                NavigateUri = new Uri(link.Url),
                FontSize = 13,
            });
        }

        LinksSection.Visibility = DetailsLinks.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
