namespace Penrose.Core.Ui;

/// <summary>zh-CN / en UI catalog. Missing keys cannot compile: both packs set every property.</summary>
public sealed record UiStrings
{
    public static UiStrings For(string? language) =>
        IsEnglish(language) ? En : Zh;

    public static bool IsEnglish(string? language) =>
        !string.IsNullOrWhiteSpace(language)
        && language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public required string IdleStatus { get; init; }
    public required string Settings { get; init; }
    public required string Fullscreen { get; init; }
    public required string ExitFullscreen { get; init; }
    public required string LeaveWindow { get; init; }
    public required string CannotPlay { get; init; }
    public required string Close { get; init; }
    public required string Open { get; init; }
    public required string OpenFile { get; init; }
    public required string OpenFolder { get; init; }
    public required string OpenUrl { get; init; }
    public required string Previous { get; init; }
    public required string Play { get; init; }
    public required string Pause { get; init; }
    public required string Next { get; init; }
    public required string AudioTracks { get; init; }
    public required string Subtitles { get; init; }
    public required string Playlist { get; init; }
    public required string Chapters { get; init; }
    public required string Night { get; init; }
    public required string AudioSystem { get; init; }
    public required string AudioStereo { get; init; }
    public required string AudioHomePcm { get; init; }
    public required string AudioBitstream { get; init; }
    public required string AudioBitstreamPcm { get; init; }
    public required string AudioPassthrough { get; init; }
    public required string AudioPassthroughHint { get; init; }
    public required string NightBlockedByPassthrough { get; init; }
    public required string Position { get; init; }
    public required string Volume { get; init; }
    public required string Video { get; init; }
    public required string PlayFailed { get; init; }
    public required string EmptyFolder { get; init; }
    public required string OpenUrlPlaceholder { get; init; }
    public required string Cancel { get; init; }
    public required string Mute { get; init; }
    public required string Unmute { get; init; }
    public required string SubOff { get; init; }
    public required string LoadExternalSub { get; init; }
    public required string SecondaryOff { get; init; }
    public required string SecondaryPrefix { get; init; }
    public required string NoTracks { get; init; }
    public required string NoChapters { get; init; }
    public required string ChapterFallback { get; init; }
    public required string PickDiscTitle { get; init; }
    public required string Skip { get; init; }
    public required string ExternalMark { get; init; }
    public required string Buffering { get; init; }
    public required string BufferingPercent { get; init; }
    public required string VolumeOsd { get; init; }
    public required string SpeedOsd { get; init; }
    public required string SubDelayOsd { get; init; }
    public required string SeekBack { get; init; }
    public required string SeekForward { get; init; }
    public required string DiscTitleOsd { get; init; }
    public required string BitstreamFallback { get; init; }
    public required string DiagnosticsExported { get; init; }
    public required string StatusTopLevel { get; init; }
    public required string StatusFullscreen { get; init; }
    public required string StatusWindow { get; init; }
    public required string FallbackBanner { get; init; }
    public required string FallbackHdrBanner { get; init; }
    public required string RememberPosition { get; init; }
    public required string RegisterDefault { get; init; }
    public required string ImportMpvConf { get; init; }
    public required string AmpWizard { get; init; }
    public required string CheckUpdates { get; init; }
    public required string Version { get; init; }
    public required string PictureQuality { get; init; }
    public required string QualityFast { get; init; }
    public required string QualityBalanced { get; init; }
    public required string QualityHigh { get; init; }
    public required string SubEncoding { get; init; }
    public required string AssOverride { get; init; }
    public required string ShortcutsHint { get; init; }
    public required string Save { get; init; }
    public required string Language { get; init; }
    public required string EngineNotReady { get; init; }
    public required string NoAudioDevices { get; init; }
    public required string AmpSinkSpeakers { get; init; }
    public required string AmpSinkHdmi { get; init; }
    public required string AmpSinkAuto { get; init; }
    public required string AmpFollowWindows { get; init; }
    public required string AmpForceStereo { get; init; }
    public required string AmpBitstream { get; init; }
    public required string AmpHtPcm { get; init; }
    public required string AmpWhere { get; init; }
    public required string AmpDevice { get; init; }
    public required string AmpTitle { get; init; }
    public required string Apply { get; init; }
    public required string AmpNoteBitstream { get; init; }
    public required string AmpNoteSpeakers { get; init; }
    public required string AmpNoteHtPcm { get; init; }
    public required string AmpNoteAuto { get; init; }
    public required string DeviceAuto { get; init; }
    public required string DevicePinned { get; init; }
    public required string MarkHdmi { get; init; }
    public required string MarkSpeakers { get; init; }
    public required string MarkAuto { get; init; }
    public required string UpdateLatest { get; init; }
    public required string UpdateAvailable { get; init; }
    public required string UpdateFailed { get; init; }
    public required string ImportApplied { get; init; }
    public required string ImportRejected { get; init; }
    public required string Library { get; init; }
    public required string BrowseLibrary { get; init; }
    public required string LibraryEmpty { get; init; }
    public required string EmbyServer { get; init; }
    public required string EmbyUser { get; init; }
    public required string EmbyPassword { get; init; }
    public required string Connect { get; init; }
    public required string Disconnect { get; init; }
    public required string ConnectedAs { get; init; }
    public required string ConnectFailed { get; init; }
    public required string ConnectBlockedByFirewall { get; init; }
    public required string ConnectBadCredentials { get; init; }
    public required string ConnectNotEmby { get; init; }
    public required string NotConnected { get; init; }
    public required string EmbyTitle { get; init; }
    public required string EmbyEntryOpen { get; init; }
    public required string EmbyEntryConnect { get; init; }
    public required string EmbyEntryHint { get; init; }
    public required string EmbyManage { get; init; }
    public required string EmbyTestConnection { get; init; }
    public required string EmbyTesting { get; init; }
    public required string EmbyConnecting { get; init; }
    public required string EmbyTestOk { get; init; }
    public required string EmbyTestLoginOk { get; init; }
    public required string EmbyTestFailed { get; init; }
    public required string EmbySessionExpired { get; init; }
    public required string Back { get; init; }
    public required string BackNav { get; init; }
    public required string Search { get; init; }
    public required string LibrarySearchPlaceholder { get; init; }
    public required string LibrarySearchTitle { get; init; }
    public required string LibraryItemCount { get; init; }
    public required string LibraryNoResults { get; init; }
    public required string KindMovie { get; init; }
    public required string KindSeries { get; init; }
    public required string KindSeason { get; init; }
    public required string KindEpisode { get; init; }
    public required string KindLibrary { get; init; }
    public required string KindBoxSet { get; init; }
    public required string KindPlaylist { get; init; }
    public required string KindFolder { get; init; }
    public required string KindMusic { get; init; }
    public required string ContinueWatching { get; init; }
    public required string ContinueWatchingRemaining { get; init; }
    public required string AllLibraries { get; init; }
    public required string NextEpisodeOsd { get; init; }
    public required string NoNextEpisode { get; init; }
    public required string NoPreviousEpisode { get; init; }
    public required string MediaServers { get; init; }
    public required string MediaServersNone { get; init; }
    public required string MediaServersEmpty { get; init; }
    public required string AddServer { get; init; }
    public required string EditServer { get; init; }
    public required string RemoveServer { get; init; }
    public required string ServerConnected { get; init; }
    public required string ServerName { get; init; }
    public required string ServerKind { get; init; }
    public required string ServerHost { get; init; }
    public required string ServerPort { get; init; }
    public required string ServerHttps { get; init; }
    public required string ServerFillHostUser { get; init; }
    public required string ResumeLast { get; init; }
    public required string VersionShort { get; init; }
    public required string SortLabel { get; init; }
    public required string SortAscending { get; init; }
    public required string SortDescending { get; init; }
    public required string SortSortName { get; init; }
    public required string SortDateCreated { get; init; }
    public required string SortPremiereDate { get; init; }
    public required string SortProductionYear { get; init; }
    public required string SortCommunityRating { get; init; }
    public required string SortRuntime { get; init; }
    public required string SortPlayCount { get; init; }
    public required string SortDatePlayed { get; init; }
    public required string SortRandom { get; init; }
    public required string PlayFromStart { get; init; }
    public required string MinutesShort { get; init; }
    public required string DetailsVideo { get; init; }
    public required string DetailsAudio { get; init; }
    public required string DetailsSubtitles { get; init; }
    public required string DetailsNone { get; init; }
    public required string BackToDetails { get; init; }
    public required string BackToHome { get; init; }
    public required string ServerFileMissing { get; init; }
    public required string SectionNextUp { get; init; }
    public required string SectionSeasons { get; init; }
    public required string SectionEpisodes { get; init; }
    public required string SectionContents { get; init; }
    public required string SectionPeople { get; init; }
    public required string SectionOverview { get; init; }
    public required string SectionArt { get; init; }
    public required string SectionSimilar { get; init; }
    public required string SectionLinks { get; init; }
    public required string MarkPlayed { get; init; }
    public required string MarkedPlayed { get; init; }
    public required string Favorite { get; init; }
    public required string Favorited { get; init; }
    public required string StartPlaying { get; init; }
    public required string StatusEnded { get; init; }
    public required string StatusContinuing { get; init; }
    public required string RoleActor { get; init; }
    public required string RoleDirector { get; init; }
    public required string RoleWriter { get; init; }
    public required string RoleProducer { get; init; }
    public required string RoleGuestStar { get; init; }
    public required string RoleComposer { get; init; }
    public required string PremiereLabel { get; init; }
    public required string StudiosLabel { get; init; }
    public required string NoSubtitles { get; init; }
    public required string SubtitleCount { get; init; }
    public required string DefaultTrack { get; init; }
    public required string ShowAll { get; init; }
    public required string ShowLess { get; init; }
    public required string VersionLabel { get; init; }
    public required string AudioTrackLabel { get; init; }
    public required string SubtitleTrackLabel { get; init; }
    public required string ExternalTrack { get; init; }
    public required string MatchDisplayRefresh { get; init; }
    public required string RefreshHzOsd { get; init; }
    public required string AutoEnableWindowsHdr { get; init; }
    public required string HdrOnOsd { get; init; }
    public required string SeekThumbnails { get; init; }
    public required string GamepadEnabled { get; init; }
    public required string AllowServerFilePaths { get; init; }
    public required string EmptyTitle { get; init; }
    public required string EmptyHint { get; init; }
    public required string RecentHeader { get; init; }
    public required string Info { get; init; }
    public required string InfoPlayback { get; init; }
    public required string InfoSource { get; init; }
    public required string InfoVideo { get; init; }
    public required string InfoOutput { get; init; }
    public required string InfoAudio { get; init; }
    public required string InfoKindUnknown { get; init; }
    public required string InfoKindLocalFile { get; init; }
    public required string InfoKindLocalDisc { get; init; }
    public required string InfoKindNetworkShare { get; init; }
    public required string InfoKindStrmDirect { get; init; }
    public required string InfoKindStrmRelay { get; init; }
    public required string InfoKindServerDirect { get; init; }
    public required string InfoKindServerTranscode { get; init; }
    public required string InfoLabelCodec { get; init; }
    public required string InfoLabelContainer { get; init; }
    public required string InfoLabelSize { get; init; }
    public required string InfoLabelBitrate { get; init; }
    public required string InfoLabelChannels { get; init; }
    public required string InfoLabelSampleRate { get; init; }
    public required string InfoLabelFps { get; init; }
    public required string InfoLabelResolution { get; init; }
    public required string InfoLabelRange { get; init; }
    public required string InfoLabelUri { get; init; }
    public required string InfoValueDash { get; init; }
    public required string InfoFpsWithEffective { get; init; }
    public required string InfoRangeSdr { get; init; }
    public required string InfoRangeHdr10 { get; init; }
    public required string InfoRangeHdr10Plus { get; init; }
    public required string InfoRangeHlg { get; init; }
    public required string InfoRangeDolbyVision { get; init; }
    public required string AudioOutput { get; init; }
    public required string ClosePane { get; init; }
    public required string Channels { get; init; }
    public required string ChannelsFollowPolicy { get; init; }
    public required string ChannelsSource { get; init; }
    public required string ChannelsStereo { get; init; }
    public required string ChannelsOsd { get; init; }
    public required string ChannelsBitstream { get; init; }
    public required string DynamicRange { get; init; }
    public required string FullscreenProgressLine { get; init; }
    public required string FormatBadgesTitle { get; init; }
    public required string PictureInPicture { get; init; }
    public required string StatusPip { get; init; }

    public static readonly UiStrings Zh = new()
    {
        IdleStatus = "打开文件、文件夹、URL 或拖入视频",
        Settings = "设置",
        Fullscreen = "全屏",
        ExitFullscreen = "退出全屏",
        LeaveWindow = "返回窗口",
        CannotPlay = "无法播放",
        Close = "关闭",
        Open = "打开",
        OpenFile = "打开文件",
        OpenFolder = "打开文件夹",
        OpenUrl = "打开 URL",
        Previous = "上一",
        Play = "播放",
        Pause = "暂停",
        Next = "下一",
        AudioTracks = "音轨",
        Subtitles = "字幕",
        Playlist = "列表",
        Chapters = "章节",
        Night = "夜间",
        AudioSystem = "系统",
        AudioStereo = "立体声",
        AudioHomePcm = "环绕 PCM",
        AudioBitstream = "位流",
        AudioBitstreamPcm = "位流→PCM",
        AudioPassthrough = "音频直通",
        AudioPassthroughHint = "AC3 / E-AC3 / DTS / TrueHD 以位流交给功放解码（WASAPI 独占，软件音量可能无效）；其他编码仍由播放器解码。开启后夜间模式不可用。",
        NightBlockedByPassthrough = "音频直通开启时无法使用夜间模式",
        Position = "进度",
        Volume = "音量",
        Video = "画面",
        PlayFailed = "播放失败",
        EmptyFolder = "文件夹里没有可播放的文件",
        OpenUrlPlaceholder = "https:// 或 http://",
        Cancel = "取消",
        Mute = "静音",
        Unmute = "取消静音",
        SubOff = "关闭字幕",
        LoadExternalSub = "加载外挂字幕…",
        SecondaryOff = "关闭次字幕",
        SecondaryPrefix = "次  ",
        NoTracks = "没有可用轨道",
        NoChapters = "没有章节",
        ChapterFallback = "章节 {0}",
        PickDiscTitle = "选择碟片标题",
        Skip = "跳过",
        ExternalMark = " 外挂",
        Buffering = "缓冲中",
        BufferingPercent = "缓冲中  {0}%",
        VolumeOsd = "音量  {0}",
        SpeedOsd = "倍速  {0}×",
        SubDelayOsd = "字幕  {0} s",
        SeekBack = "−5 秒",
        SeekForward = "+5 秒",
        DiscTitleOsd = "Title {0}",
        BitstreamFallback = "位流未成功，已回退为 PCM（{0}）",
        DiagnosticsExported = "诊断已导出: {0}",
        StatusTopLevel = "顶层全屏",
        StatusFullscreen = "全屏",
        StatusWindow = "窗口",
        FallbackBanner = "已进入顶层全屏，窗口内控件暂不可用。按 Esc 返回。",
        FallbackHdrBanner = "窗口无法输出 HDR10，已改用顶层全屏。控件暂不可用。Esc 返回。",
        RememberPosition = "记住播放位置",
        RegisterDefault = "注册为当前用户的默认播放器（常见视频格式）",
        ImportMpvConf = "导入 mpv.conf…",
        AmpWizard = "功放 / 回音壁向导…",
        CheckUpdates = "检查更新",
        Version = "版本  {0}",
        PictureQuality = "画面质量",
        QualityFast = "快速",
        QualityBalanced = "均衡",
        QualityHigh = "高质量",
        SubEncoding = "字幕编码",
        AssOverride = "ASS 样式覆盖",
        ShortcutsHint = "[ ] 倍速　　Z / X 字幕延迟　　滚轮音量　　双击全屏　　P 画中画",
        Save = "保存",
        Language = "语言",
        EngineNotReady = "播放器尚未就绪",
        NoAudioDevices = "没有可用的音频设备",
        AmpSinkSpeakers = "电脑扬声器或耳机",
        AmpSinkHdmi = "HDMI 功放 / 回音壁",
        AmpSinkAuto = "自动（Windows 首选）",
        AmpFollowWindows = "跟随 Windows 声道",
        AmpForceStereo = "强制立体声",
        AmpBitstream = "位流直通（功放解码 Atmos / TrueHD / DTS-HD）",
        AmpHtPcm = "家庭影院 PCM（播放器解码）",
        AmpWhere = "声音从哪出来？",
        AmpDevice = "音频设备",
        AmpTitle = "功放 / 回音壁",
        Apply = "应用",
        AmpNoteBitstream = "位流使用 WASAPI 独占。夜间模式会关闭，软件音量可能无效。失败时回退 PCM 并提示。不会软件渲染 Atmos 对象。",
        AmpNoteSpeakers = "固定到扬声器或耳机，避免自动选到 HDMI。系统兼容跟随 Windows 声道；强制立体声走解码器下混。",
        AmpNoteHtPcm = "播放器解码为 7.1/5.1/立体声 PCM。共享模式，夜间模式仍可用。",
        AmpNoteAuto = "跟随 Windows 首选设备。注意：auto 可能先选到 HDMI 输出，而不是笔记本扬声器。",
        DeviceAuto = "自动设备",
        DevicePinned = "已固定音频设备",
        MarkHdmi = "HDMI  ",
        MarkSpeakers = "扬声器  ",
        MarkAuto = "自动  ",
        UpdateLatest = "已是最新版本  {0}",
        UpdateAvailable = "发现新版本 {0}（当前 {1}），请到 {2} 下载。",
        UpdateFailed = "检查失败：{0}",
        ImportApplied = "已应用 {0} 项",
        ImportRejected = "已应用 {0} 项，拒绝 {1} 项（结构性/未知选项）",
        Library = "库",
        BrowseLibrary = "浏览 Emby",
        LibraryEmpty = "媒体库是空的，或尚未连接服务器",
        EmbyServer = "Emby 地址",
        EmbyUser = "用户名",
        EmbyPassword = "密码",
        Connect = "连接",
        Disconnect = "断开",
        ConnectedAs = "已连接  {0}",
        ConnectFailed = "连接失败：{0}",
        ConnectBlockedByFirewall = "服务器的 Cloudflare 防火墙拒绝了当前网络的出口 IP（HTTP 403）。请检查代理 / VPN 的出口节点或分流规则，或联系服务器管理员。",
        ConnectBadCredentials = "用户名或密码不正确（HTTP 401）。",
        ConnectNotEmby = "该地址返回的是网页而不是 Emby 接口，请检查地址（含端口和子路径）。",
        NotConnected = "未连接 Emby",
        EmbyTitle = "Emby 服务器",
        EmbyEntryOpen = "进入 Emby 媒体库",
        EmbyEntryConnect = "连接 Emby 服务器",
        EmbyEntryHint = "登录后会记住账户，下次直接进入",
        EmbyManage = "账户与服务器",
        EmbyTestConnection = "测试连接",
        EmbyTesting = "正在测试连接…",
        EmbyConnecting = "正在连接…",
        EmbyTestOk = "连接成功：{0}（Emby {1}）",
        EmbyTestLoginOk = "登录验证通过：{0}",
        EmbyTestFailed = "连接失败：{0}",
        EmbySessionExpired = "登录已过期，请重新连接 Emby",
        Back = "返回上一级",
        BackNav = "返回",
        Search = "搜索",
        LibrarySearchPlaceholder = "搜索 Emby 媒体库…",
        LibrarySearchTitle = "搜索：{0}",
        LibraryItemCount = "{0} 项",
        LibraryNoResults = "没有找到匹配的内容",
        KindMovie = "电影",
        KindSeries = "剧集",
        KindSeason = "季",
        KindEpisode = "单集",
        KindLibrary = "媒体库",
        KindBoxSet = "合集",
        KindPlaylist = "播放列表",
        KindFolder = "文件夹",
        KindMusic = "音乐",
        ContinueWatching = "继续观看",
        ContinueWatchingRemaining = "继续观看 · 剩 {0} 分钟",
        AllLibraries = "媒体库",
        NextEpisodeOsd = "下一集：{0}",
        NoNextEpisode = "已经是最后一集",
        NoPreviousEpisode = "已经是第一集",
        MediaServers = "影视服务器",
        MediaServersNone = "尚未添加服务器",
        MediaServersEmpty = "还没有影视服务器。点击下方“添加服务器”，填写 Emby 的地址和账号。",
        AddServer = "添加服务器",
        EditServer = "编辑服务器",
        RemoveServer = "移除",
        ServerConnected = "已连接",
        ServerName = "名称",
        ServerKind = "通讯协议",
        ServerHost = "地址",
        ServerPort = "端口",
        ServerHttps = "使用 HTTPS",
        ServerFillHostUser = "请填写地址和用户名",
        ResumeLast = "继续播放 {0}",
        VersionShort = "版本 {0}",
        SortLabel = "排序",
        SortAscending = "升序",
        SortDescending = "降序",
        SortSortName = "名称",
        SortDateCreated = "添加日期",
        SortPremiereDate = "首播日期",
        SortProductionYear = "年份",
        SortCommunityRating = "评分",
        SortRuntime = "时长",
        SortPlayCount = "播放次数",
        SortDatePlayed = "最近播放",
        SortRandom = "随机",
        PlayFromStart = "从头播放",
        MinutesShort = "{0} 分钟",
        DetailsVideo = "视频",
        DetailsAudio = "音频",
        DetailsSubtitles = "字幕",
        DetailsNone = "无",
        BackToDetails = "停止并返回",
        BackToHome = "返回主页",
        ServerFileMissing = "服务器上找不到这个文件（HTTP 404），可能已被移动或删除",
        SectionNextUp = "接下来",
        SectionSeasons = "季",
        SectionEpisodes = "分集",
        SectionContents = "内容",
        SectionPeople = "演职人员",
        SectionOverview = "剧情简介",
        SectionArt = "艺术图",
        SectionSimilar = "更多类似",
        SectionLinks = "外部链接",
        MarkPlayed = "标记为已播",
        MarkedPlayed = "已播放",
        Favorite = "收藏",
        Favorited = "已收藏",
        StartPlaying = "开始播放",
        StatusEnded = "已完结",
        StatusContinuing = "连载中",
        RoleActor = "演员",
        RoleDirector = "导演",
        RoleWriter = "编剧",
        RoleProducer = "制片",
        RoleGuestStar = "客串",
        RoleComposer = "作曲",
        PremiereLabel = "首播",
        StudiosLabel = "出品",
        NoSubtitles = "无字幕",
        SubtitleCount = "{0} 条字幕",
        DefaultTrack = "默认",
        ShowAll = "显示全部",
        ShowLess = "收起",
        VersionLabel = "版本",
        AudioTrackLabel = "音轨",
        SubtitleTrackLabel = "字幕",
        ExternalTrack = "外挂",
        MatchDisplayRefresh = "匹配显示器刷新率（播完还原）",
        RefreshHzOsd = "{0} Hz",
        AutoEnableWindowsHdr = "HDR 片源时打开 Windows HDR（播完还原）",
        HdrOnOsd = "Windows HDR",
        SeekThumbnails = "进度条缩略图",
        GamepadEnabled = "手柄 / 遥控（Xbox：A 播放，LB/RB 上一/下一）",
        AllowServerFilePaths = "允许直接打开媒体服务器给出的文件路径（UNC / 本地盘，仅限可信服务器）",
        EmptyTitle = "开始播放",
        EmptyHint = "拖入视频，或从下面选择来源。支持文件夹连播、Blu-ray/DVD 目录、.strm 与网络地址。",
        RecentHeader = "最近播放",
        Info = "播放信息",
        InfoPlayback = "播放类型",
        InfoSource = "媒体源",
        InfoVideo = "视频",
        InfoOutput = "输出",
        InfoAudio = "音频",
        InfoKindUnknown = "未知",
        InfoKindLocalFile = "本地播放",
        InfoKindLocalDisc = "光盘播放",
        InfoKindNetworkShare = "网络共享",
        InfoKindStrmDirect = "strm 直连",
        InfoKindStrmRelay = "strm 中继",
        InfoKindServerDirect = "服务器直连",
        InfoKindServerTranscode = "服务器转码",
        InfoLabelCodec = "编码",
        InfoLabelContainer = "封装",
        InfoLabelSize = "大小",
        InfoLabelBitrate = "码率",
        InfoLabelChannels = "声道",
        InfoLabelSampleRate = "采样率",
        InfoLabelFps = "帧率",
        InfoLabelResolution = "分辨率",
        InfoLabelRange = "动态范围",
        InfoLabelUri = "地址",
        InfoValueDash = "—",
        InfoFpsWithEffective = "{0} → {1} fps",
        InfoRangeSdr = "SDR",
        InfoRangeHdr10 = "HDR10",
        InfoRangeHdr10Plus = "HDR10+",
        InfoRangeHlg = "HLG",
        InfoRangeDolbyVision = "Dolby Vision",
        AudioOutput = "音频输出",
        ClosePane = "关闭面板",
        Channels = "声道",
        ChannelsFollowPolicy = "跟随音频策略",
        ChannelsSource = "按源声道输出",
        ChannelsStereo = "立体声 2.0",
        ChannelsOsd = "声道：{0}",
        ChannelsBitstream = "位流直通时由功放解码，无法下混",
        DynamicRange = "动态范围",
        FullscreenProgressLine = "全屏隐藏控件后仍显示底部进度线",
        FormatBadgesTitle = "格式",
        PictureInPicture = "画中画",
        StatusPip = "画中画",
    };

    public static readonly UiStrings En = new()
    {
        IdleStatus = "Open a file, folder, URL, or drop a video",
        Settings = "Settings",
        Fullscreen = "Full screen",
        ExitFullscreen = "Exit full screen",
        LeaveWindow = "Back to window",
        CannotPlay = "Can't play",
        Close = "Close",
        Open = "Open",
        OpenFile = "Open file",
        OpenFolder = "Open folder",
        OpenUrl = "Open URL",
        Previous = "Prev",
        Play = "Play",
        Pause = "Pause",
        Next = "Next",
        AudioTracks = "Audio",
        Subtitles = "Subs",
        Playlist = "List",
        Chapters = "Ch.",
        Night = "Night",
        AudioSystem = "System",
        AudioStereo = "Stereo",
        AudioHomePcm = "Surround",
        AudioBitstream = "Bitstream",
        AudioBitstreamPcm = "Bitstream→PCM",
        AudioPassthrough = "Audio passthrough",
        AudioPassthroughHint = "AC3 / E-AC3 / DTS / TrueHD are sent as a bitstream for the receiver to decode (WASAPI exclusive; software volume may not work); other codecs are still decoded by the player. Night mode is unavailable while on.",
        NightBlockedByPassthrough = "Night mode is unavailable while audio passthrough is on",
        Position = "Position",
        Volume = "Volume",
        Video = "Video",
        PlayFailed = "Playback failed",
        EmptyFolder = "No playable files in this folder",
        OpenUrlPlaceholder = "https:// or http://",
        Cancel = "Cancel",
        Mute = "Mute",
        Unmute = "Unmute",
        SubOff = "Turn off subtitles",
        LoadExternalSub = "Load external subtitle…",
        SecondaryOff = "Turn off secondary subtitles",
        SecondaryPrefix = "2nd  ",
        NoTracks = "No tracks",
        NoChapters = "No chapters",
        ChapterFallback = "Chapter {0}",
        PickDiscTitle = "Choose disc title",
        Skip = "Skip",
        ExternalMark = " ext",
        Buffering = "Buffering",
        BufferingPercent = "Buffering  {0}%",
        VolumeOsd = "Volume  {0}",
        SpeedOsd = "Speed  {0}×",
        SubDelayOsd = "Subs  {0} s",
        SeekBack = "−5 s",
        SeekForward = "+5 s",
        DiscTitleOsd = "Title {0}",
        BitstreamFallback = "Bitstream failed, fell back to PCM ({0})",
        DiagnosticsExported = "Diagnostics exported: {0}",
        StatusTopLevel = "Top-level FS",
        StatusFullscreen = "Full screen",
        StatusWindow = "Window",
        FallbackBanner = "Top-level full screen. In-window controls are unavailable. Press Esc to return.",
        FallbackHdrBanner = "This window cannot output HDR10, so top-level full screen is used. Esc to return.",
        RememberPosition = "Remember playback position",
        RegisterDefault = "Register as this user's default player (common video types)",
        ImportMpvConf = "Import mpv.conf…",
        AmpWizard = "AVR / soundbar wizard…",
        CheckUpdates = "Check for updates",
        Version = "Version  {0}",
        PictureQuality = "Picture quality",
        QualityFast = "Fast",
        QualityBalanced = "Balanced",
        QualityHigh = "High",
        SubEncoding = "Subtitle encoding",
        AssOverride = "ASS style override",
        ShortcutsHint = "[ ] speed    Z / X sub delay    wheel volume    double-click full screen    P picture-in-picture",
        Save = "Save",
        Language = "Language",
        EngineNotReady = "Player is not ready yet",
        NoAudioDevices = "No audio devices",
        AmpSinkSpeakers = "PC speakers or headphones",
        AmpSinkHdmi = "HDMI AVR / soundbar",
        AmpSinkAuto = "Auto (Windows default)",
        AmpFollowWindows = "Follow Windows layout",
        AmpForceStereo = "Force stereo",
        AmpBitstream = "Bitstream (AVR decodes Atmos / TrueHD / DTS-HD)",
        AmpHtPcm = "Home-theater PCM (player decodes)",
        AmpWhere = "Where should sound go?",
        AmpDevice = "Audio device",
        AmpTitle = "AVR / soundbar",
        Apply = "Apply",
        AmpNoteBitstream = "Bitstream uses exclusive WASAPI. Night mode turns off and software volume may not work. Failure falls back to PCM. Atmos objects are not rendered in software.",
        AmpNoteSpeakers = "Pin speakers/headphones so auto does not pick HDMI. System follows Windows layout; force stereo downmixes in the decoder.",
        AmpNoteHtPcm = "Player decodes to 7.1/5.1/stereo PCM. Shared mode; night mode still works.",
        AmpNoteAuto = "Follow Windows default. Note: auto may pick an HDMI output before laptop speakers.",
        DeviceAuto = "Auto device",
        DevicePinned = "Audio device pinned",
        MarkHdmi = "HDMI  ",
        MarkSpeakers = "Speakers  ",
        MarkAuto = "Auto  ",
        UpdateLatest = "Up to date  {0}",
        UpdateAvailable = "Found {0} (current {1}). Download it from {2}.",
        UpdateFailed = "Check failed: {0}",
        ImportApplied = "Applied {0} option(s)",
        ImportRejected = "Applied {0} option(s), rejected {1} (structural/unknown)",
        Library = "Library",
        BrowseLibrary = "Browse Emby",
        LibraryEmpty = "Library is empty, or the server is not connected",
        EmbyServer = "Emby URL",
        EmbyUser = "User name",
        EmbyPassword = "Password",
        Connect = "Connect",
        Disconnect = "Disconnect",
        ConnectedAs = "Connected  {0}",
        ConnectFailed = "Connect failed: {0}",
        ConnectBlockedByFirewall = "The server's Cloudflare firewall refused this network's egress IP (HTTP 403). Check the proxy / VPN exit or routing rules, or ask the server admin.",
        ConnectBadCredentials = "User name or password rejected (HTTP 401).",
        ConnectNotEmby = "That address returned a web page instead of the Emby API; check the URL (port and sub-path).",
        NotConnected = "Not connected to Emby",
        EmbyTitle = "Emby server",
        EmbyEntryOpen = "Open Emby library",
        EmbyEntryConnect = "Connect to an Emby server",
        EmbyEntryHint = "The account is remembered; next time this opens the library directly",
        EmbyManage = "Account & server",
        EmbyTestConnection = "Test connection",
        EmbyTesting = "Testing connection…",
        EmbyConnecting = "Connecting…",
        EmbyTestOk = "Connected: {0} (Emby {1})",
        EmbyTestLoginOk = "Sign-in verified: {0}",
        EmbyTestFailed = "Connection failed: {0}",
        EmbySessionExpired = "The Emby sign-in has expired; please connect again",
        Back = "Back",
        BackNav = "Back",
        Search = "Search",
        LibrarySearchPlaceholder = "Search the Emby library…",
        LibrarySearchTitle = "Search: {0}",
        LibraryItemCount = "{0} items",
        LibraryNoResults = "Nothing matches",
        KindMovie = "Movie",
        KindSeries = "Series",
        KindSeason = "Season",
        KindEpisode = "Episode",
        KindLibrary = "Library",
        KindBoxSet = "Collection",
        KindPlaylist = "Playlist",
        KindFolder = "Folder",
        KindMusic = "Music",
        ContinueWatching = "Continue watching",
        ContinueWatchingRemaining = "Continue · {0} min left",
        AllLibraries = "Libraries",
        NextEpisodeOsd = "Next episode: {0}",
        NoNextEpisode = "This is the last episode",
        NoPreviousEpisode = "This is the first episode",
        MediaServers = "Media servers",
        MediaServersNone = "No server added yet",
        MediaServersEmpty = "No media servers yet. Use \"Add server\" below and enter the Emby address and account.",
        AddServer = "Add server",
        EditServer = "Edit server",
        RemoveServer = "Remove",
        ServerConnected = "Connected",
        ServerName = "Name",
        ServerKind = "Protocol",
        ServerHost = "Address",
        ServerPort = "Port",
        ServerHttps = "Use HTTPS",
        ServerFillHostUser = "Enter the address and user name",
        ResumeLast = "Resume {0}",
        VersionShort = "Version {0}",
        SortLabel = "Sort",
        SortAscending = "Ascending",
        SortDescending = "Descending",
        SortSortName = "Name",
        SortDateCreated = "Date added",
        SortPremiereDate = "Premiere date",
        SortProductionYear = "Year",
        SortCommunityRating = "Rating",
        SortRuntime = "Runtime",
        SortPlayCount = "Play count",
        SortDatePlayed = "Last played",
        SortRandom = "Random",
        PlayFromStart = "Play from start",
        MinutesShort = "{0} min",
        DetailsVideo = "Video",
        DetailsAudio = "Audio",
        DetailsSubtitles = "Subtitles",
        DetailsNone = "none",
        BackToDetails = "Stop and go back",
        BackToHome = "Back to home",
        ServerFileMissing = "The server has no file for this item (HTTP 404); it may have been moved or deleted",
        SectionNextUp = "Next up",
        SectionSeasons = "Seasons",
        SectionEpisodes = "Episodes",
        SectionContents = "Contents",
        SectionPeople = "Cast & crew",
        SectionOverview = "Overview",
        SectionArt = "Art",
        SectionSimilar = "More like this",
        SectionLinks = "Links",
        MarkPlayed = "Mark played",
        MarkedPlayed = "Played",
        Favorite = "Favourite",
        Favorited = "Favourited",
        StartPlaying = "Play",
        StatusEnded = "Ended",
        StatusContinuing = "Continuing",
        RoleActor = "Actor",
        RoleDirector = "Director",
        RoleWriter = "Writer",
        RoleProducer = "Producer",
        RoleGuestStar = "Guest star",
        RoleComposer = "Composer",
        PremiereLabel = "Premiere",
        StudiosLabel = "Studios",
        NoSubtitles = "No subtitles",
        SubtitleCount = "{0} subtitle tracks",
        DefaultTrack = "default",
        ShowAll = "Show all",
        ShowLess = "Show less",
        VersionLabel = "Version",
        AudioTrackLabel = "Audio",
        SubtitleTrackLabel = "Subtitles",
        ExternalTrack = "external",
        MatchDisplayRefresh = "Match display refresh rate (restore when idle)",
        RefreshHzOsd = "{0} Hz",
        AutoEnableWindowsHdr = "Turn on Windows HDR for HDR files (restore when idle)",
        HdrOnOsd = "Windows HDR",
        SeekThumbnails = "Seek-bar thumbnails",
        GamepadEnabled = "Gamepad / remote (Xbox: A play, LB/RB prev/next)",
        AllowServerFilePaths = "Open media-server file paths directly (UNC / local drives; trusted servers only)",
        EmptyTitle = "Start playing",
        EmptyHint = "Drop a video onto the window or pick a source below. Folders play in sequence; Blu-ray/DVD folders, .strm and URLs are supported.",
        RecentHeader = "Recently played",
        Info = "Playback info",
        InfoPlayback = "Playback",
        InfoSource = "Source",
        InfoVideo = "Video",
        InfoOutput = "Output",
        InfoAudio = "Audio",
        InfoKindUnknown = "Unknown",
        InfoKindLocalFile = "Local file",
        InfoKindLocalDisc = "Disc",
        InfoKindNetworkShare = "Network share",
        InfoKindStrmDirect = ".strm direct",
        InfoKindStrmRelay = ".strm relay",
        InfoKindServerDirect = "Server direct play",
        InfoKindServerTranscode = "Server transcode",
        InfoLabelCodec = "Codec",
        InfoLabelContainer = "Container",
        InfoLabelSize = "Size",
        InfoLabelBitrate = "Bitrate",
        InfoLabelChannels = "Channels",
        InfoLabelSampleRate = "Sample rate",
        InfoLabelFps = "Frame rate",
        InfoLabelResolution = "Resolution",
        InfoLabelRange = "Dynamic range",
        InfoLabelUri = "URI",
        InfoValueDash = "—",
        InfoFpsWithEffective = "{0} → {1} fps",
        InfoRangeSdr = "SDR",
        InfoRangeHdr10 = "HDR10",
        InfoRangeHdr10Plus = "HDR10+",
        InfoRangeHlg = "HLG",
        InfoRangeDolbyVision = "Dolby Vision",
        AudioOutput = "Audio output",
        ClosePane = "Close pane",
        Channels = "Channels",
        ChannelsFollowPolicy = "Follow audio policy",
        ChannelsSource = "Source layout",
        ChannelsStereo = "Stereo 2.0",
        ChannelsOsd = "Channels: {0}",
        ChannelsBitstream = "Bitstream passthrough is decoded by the receiver; no downmix",
        DynamicRange = "Dynamic range",
        FullscreenProgressLine = "Keep the bottom progress line in fullscreen after controls hide",
        FormatBadgesTitle = "Format",
        PictureInPicture = "Picture-in-picture",
        StatusPip = "PiP",
    };
}
