using MediaPlayer.Core.Playback;
using MediaPlayer.Core.Sources;
using MediaPlayer.Sources.Emby;

namespace MediaPlayer.Sources.Emby.Tests;

public sealed class EmbyItemsParserTests
{
    [Fact]
    public void Parses_items_array()
    {
        IReadOnlyList<LibraryItem> items = EmbyItemsParser.Parse(
            """
            {"Items":[
              {"Id":"a1","Name":"Movie A","Type":"Movie","Path":"https://cdn.example/a.strm"},
              {"Id":"e2","Name":"S01E01","Type":"Episode"}
            ]}
            """);
        Assert.Equal(2, items.Count);
        Assert.Equal("a1", items[0].Id);
        Assert.Equal("Movie", items[0].Kind);
        Assert.Equal("Episode", items[1].Kind);
        Assert.False(items[0].IsFolder);
        Assert.Empty(EmbyItemsParser.Parse(null));
        Assert.Empty(EmbyItemsParser.Parse("{not json"));
    }

    [Fact]
    public void Views_and_series_are_folders()
    {
        IReadOnlyList<LibraryItem> items = EmbyItemsParser.Parse(
            """
            {"Items":[
              {"Id":"v1","Name":"Movies","Type":"CollectionFolder","CollectionType":"movies","IsFolder":true},
              {"Id":"s1","Name":"One Room","Type":"Series","ProductionYear":2017},
              {"Id":"e1","Name":"S00E01","Type":"Episode","IndexNumber":1,"ParentIndexNumber":0,"SeriesName":"One Room"}
            ]}
            """);
        Assert.True(items[0].IsFolder);
        Assert.Equal("movies", items[0].CollectionType);
        Assert.True(items[1].IsFolder);
        Assert.Equal(2017, items[1].ProductionYear);
        Assert.False(items[2].IsFolder);
        Assert.Equal("S00E01", items[2].EpisodeCode);
        Assert.Equal(2, EmbyItemsParser.TotalCount("""{"Items":[],"TotalRecordCount":2}"""));
    }

    [Fact]
    public void Resume_point_runtime_and_parents()
    {
        IReadOnlyList<LibraryItem> items = EmbyItemsParser.Parse(
            """
            {"Items":[
              {"Id":"e1","Name":"Ep","Type":"Episode","SeriesId":"s1","SeasonId":"se1","ParentId":"se1","IndexNumber":3,"ParentIndexNumber":2,
               "RunTimeTicks":15000000000,"UserData":{"PlayedPercentage":40,"PlaybackPositionTicks":6000000000,"Played":false}}
            ]}
            """);
        LibraryItem ep = items[0];
        Assert.Equal("se1", ep.SeasonId);
        Assert.Equal("se1", ep.ParentId);
        Assert.Equal(TimeSpan.FromMinutes(25), ep.RunTime);
        Assert.Equal(TimeSpan.FromMinutes(10), ep.ResumePosition);
        Assert.Equal(TimeSpan.FromMinutes(15), ep.Remaining);
        Assert.Null(EmbyItemsParser.Parse("""{"Items":[{"Id":"x","Name":"Bare","Type":"Movie"}]}""")[0].Remaining);
    }

    [Fact]
    public void Poster_tags_progress_and_image_urls()
    {
        IReadOnlyList<LibraryItem> items = EmbyItemsParser.Parse(
            """
            {"Items":[
              {"Id":"m1","Name":"Movie","Type":"Movie","ImageTags":{"Primary":"abc"},"UserData":{"PlayedPercentage":42.5,"Played":false}},
              {"Id":"e1","Name":"Ep","Type":"Episode","SeriesId":"s1","SeriesPrimaryImageTag":"ser","UserData":{"Played":true}},
              {"Id":"x1","Name":"Bare","Type":"Movie"}
            ]}
            """);
        Assert.Equal("abc", items[0].PrimaryImageTag);
        Assert.Equal(42.5, items[0].PlayedPercentage);
        Assert.False(items[0].Played);
        Assert.False(items[0].HasLandscapeArt);
        Assert.Null(items[1].PrimaryImageTag);
        Assert.Equal("s1", items[1].SeriesId);
        Assert.True(items[1].Played);
        Assert.True(items[1].HasLandscapeArt);

        using EmbyClient client = new(new Uri("https://emby.example/media"));
        client.SetAccessToken("tok");
        EmbyContentProvider provider = new(client, "u1");
        Uri own = provider.ImageUrl(items[0], 300, 450)!;
        Assert.Equal("https://emby.example/media/emby/Items/m1/Images/Primary?quality=90&maxWidth=300&maxHeight=450&tag=abc&api_key=tok", own.AbsoluteUri);
        Uri series = provider.ImageUrl(items[1], 300, 450)!;
        Assert.Contains("/Items/s1/Images/Primary", series.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("tag=ser", series.AbsoluteUri, StringComparison.Ordinal);
        Assert.Null(provider.ImageUrl(items[2], 300, 450));
    }

    [Fact]
    public void Details_carry_overview_genres_streams_and_backdrop()
    {
        LibraryItemDetails? details = EmbyItemsParser.ParseDetails(
            """
            {
              "Id":"m1","Name":"Akira","Type":"Movie","ProductionYear":1988,"RunTimeTicks":74880000000,
              "Overview":"Neo-Tokyo.","Taglines":["Neo-Tokyo is about to explode."],
              "Genres":["Animation","Sci-Fi"],"Studios":[{"Name":"TMS","Id":1}],
              "OfficialRating":"R","CommunityRating":8.0,"PremiereDate":"1988-07-16T00:00:00.0000000Z",
              "Container":"mkv",
              "BackdropImageTags":["bd1"],
              "MediaSources":[{"Id":"ms1","Container":"mkv","MediaStreams":[
                {"Type":"Video","Codec":"hevc","Width":1920,"Height":1080,"VideoRange":"HDR","IsDefault":true},
                {"Type":"Audio","Codec":"truehd","Language":"jpn","Channels":6,"ChannelLayout":"5.1","DisplayTitle":"Japanese TrueHD 5.1"},
                {"Type":"Subtitle","Codec":"srt","Language":"chi","DisplayTitle":"Chinese (SRT)","IsExternal":true}
              ]}]
            }
            """);
        Assert.NotNull(details);
        Assert.Equal("Akira", details!.Item.Name);
        Assert.Equal("Neo-Tokyo.", details.Overview);
        Assert.Equal("Neo-Tokyo is about to explode.", details.Tagline);
        Assert.Equal(["Animation", "Sci-Fi"], details.Genres);
        Assert.Equal(["TMS"], details.Studios);
        Assert.Equal("R", details.OfficialRating);
        Assert.Equal(8.0, details.CommunityRating);
        Assert.Equal(1988, details.PremiereDate?.Year);
        Assert.Equal("mkv", details.Container);
        Assert.Equal(3, details.Streams.Count);
        Assert.Equal(("Video", "hevc", 1920, "HDR"), (details.Streams[0].Type, details.Streams[0].Codec, details.Streams[0].Width, details.Streams[0].VideoRange));
        Assert.Equal(("jpn", 6, "5.1"), (details.Streams[1].Language, details.Streams[1].Channels, details.Streams[1].ChannelLayout));
        Assert.True(details.Streams[2].IsExternal);
        Assert.Equal("m1", details.BackdropItemId);
        Assert.Equal("bd1", details.BackdropTag);
        Assert.Null(EmbyItemsParser.ParseDetails(null));
        Assert.Null(EmbyItemsParser.ParseDetails("{not json"));
    }

    [Fact]
    public void Details_carry_people_links_source_and_flags()
    {
        LibraryItemDetails? details = EmbyItemsParser.ParseDetails(
            """
            {
              "Id":"s1","Name":"Black Mirror","OriginalTitle":"Black Mirror","Type":"Series","Status":"Ended",
              "PremiereDate":"2011-12-04T00:00:00.0000000Z","EndDate":"2019-06-05T00:00:00.0000000Z",
              "BackdropImageTags":["b1","b2","b3"],
              "UserData":{"Played":false,"IsFavorite":true,"PlayedPercentage":0},
              "People":[
                {"Name":"Charlie Brooker","Id":"p1","Type":"Writer","PrimaryImageTag":"t1"},
                {"Name":"Rory Kinnear","Id":"p2","Role":"Michael Callow","Type":"Actor"},
                {"Name":"","Id":"p3","Type":"Actor"}
              ],
              "ExternalUrls":[{"Name":"IMDb","Url":"https://www.imdb.com/title/tt2085059"},{"Name":"Bad","Url":"javascript:alert(1)"}],
              "MediaSources":[{"Id":"ms","Name":"S01E01 - 1080p","Size":2362232012,"Bitrate":7200000,"Container":"mkv",
                "MediaStreams":[{"Type":"Video","Codec":"h264","Width":1920,"Height":1080,"AverageFrameRate":25.0}]}]
            }
            """);
        Assert.NotNull(details);
        Assert.Equal("Ended", details!.Status);
        Assert.Equal(2019, details.EndDate?.Year);
        Assert.True(details.IsFavorite);
        Assert.Equal(["b1", "b2", "b3"], details.BackdropTags);
        Assert.Equal("b1", details.BackdropTag);
        Assert.Equal(2, details.People.Count);
        Assert.Equal(("Charlie Brooker", "Writer", "t1"), (details.People[0].Name, details.People[0].Type, details.People[0].ImageTag));
        Assert.Equal("Michael Callow", details.People[1].Role);
        ExternalLink link = Assert.Single(details.ExternalUrls);
        Assert.Equal("IMDb", link.Name);
        Assert.Equal("S01E01 - 1080p", details.SourceName);
        Assert.Equal(2362232012, details.SourceSize);
        Assert.Equal(7200000, details.SourceBitrate);
        Assert.Equal(25.0, details.Streams[0].FrameRate);
        MediaSourceInfo version = Assert.Single(details.Sources);
        Assert.Equal("ms", version.Id);
        Assert.Same(details.Streams, version.Streams);
    }

    [Fact]
    public void Every_version_and_stream_index_is_kept()
    {
        LibraryItemDetails? details = EmbyItemsParser.ParseDetails(
            """
            {"Id":"m","Name":"Movie","Type":"Movie","MediaSources":[
              {"Id":"a","Name":"1080p","MediaStreams":[
                {"Type":"Video","Index":0,"Codec":"h264"},
                {"Type":"Audio","Index":1,"Language":"jpn","IsDefault":true},
                {"Type":"Audio","Index":2,"Language":"eng"},
                {"Type":"Subtitle","Index":3,"Language":"chi"},
                {"Type":"Subtitle","Index":4,"Language":"eng","IsExternal":true}]},
              {"Id":"b","Name":"4K","MediaStreams":[{"Type":"Video","Index":0,"Codec":"hevc","Width":3840}]}
            ]}
            """);
        Assert.NotNull(details);
        Assert.Equal(2, details!.Sources.Count);
        Assert.Equal(["a", "b"], details.Sources.Select(s => s.Id));
        Assert.Equal([0, 1, 2, 3, 4], details.Sources[0].Streams.Select(s => s.Index));
        Assert.True(details.Sources[0].Streams[4].IsExternal);
        Assert.Equal(3840, details.Sources[1].Streams[0].Width);
    }

    [Fact]
    public void Episode_details_fall_back_to_the_series_backdrop()
    {
        LibraryItemDetails? details = EmbyItemsParser.ParseDetails(
            """
            {"Id":"e1","Name":"Pilot","Type":"Episode","SeriesId":"s1","SeriesName":"Show",
             "ParentBackdropItemId":"s1","ParentBackdropImageTags":["sb"],"BackdropImageTags":[]}
            """);
        Assert.NotNull(details);
        Assert.Equal("s1", details!.BackdropItemId);
        Assert.Equal("sb", details.BackdropTag);
        Assert.Empty(details.Streams);
        Assert.Empty(details.Genres);
        Assert.Null(details.Tagline);
    }

    [Fact]
    public void Children_query_applies_sort_only_to_recursive_library_listings()
    {
        string natural = EmbyClient.ChildrenPath("u", "season1", 0, null, new LibrarySort("DateCreated", true));
        Assert.Contains("SortBy=IsFolder,ParentIndexNumber,IndexNumber,SortName&SortOrder=Ascending", natural, StringComparison.Ordinal);
        Assert.DoesNotContain("Recursive", natural, StringComparison.Ordinal);

        string sorted = EmbyClient.ChildrenPath("u", "lib", 200, "Movie", new LibrarySort("DateCreated", true));
        Assert.Contains("Recursive=true&IncludeItemTypes=Movie&SortBy=DateCreated,SortName&SortOrder=Descending", sorted, StringComparison.Ordinal);
        Assert.EndsWith("&StartIndex=200", sorted, StringComparison.Ordinal);

        string byName = EmbyClient.ChildrenPath("u", "lib", 0, "Series", null);
        Assert.Contains("SortBy=SortName&SortOrder=Ascending", byName, StringComparison.Ordinal);

        // Unknown fields fall back to the name order rather than reaching the server.
        string unknown = EmbyClient.ChildrenPath("u", "lib", 0, "Movie", new LibrarySort("DropTable", false));
        Assert.Contains("SortBy=SortName&SortOrder=Ascending", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void Sort_defaults_follow_emby_conventions()
    {
        Assert.Equal("SortName", LibrarySort.Default.Field);
        Assert.False(LibrarySort.Default.Descending);
        Assert.True(LibrarySort.DefaultsToDescending("DateCreated"));
        Assert.True(LibrarySort.DefaultsToDescending("CommunityRating"));
        Assert.False(LibrarySort.DefaultsToDescending("SortName"));
        Assert.False(LibrarySort.DefaultsToDescending("Runtime"));
        Assert.Contains("Random", LibrarySort.Fields);
    }

    [Fact]
    public void Session_payload_uses_emby_ticks()
    {
        ReportingContext context = new("emby", "item-9", "sess-1", null);
        EmbyProgressBody payload = EmbySessionPayload.Create(context, TimeSpan.FromSeconds(12.5), paused: true);
        Assert.Equal("item-9", payload.ItemId);
        Assert.Equal("sess-1", payload.PlaySessionId);
        Assert.Equal(125_000_000L, payload.PositionTicks);
        Assert.True(payload.IsPaused);
        Assert.True(payload.CanSeek);
    }
}
