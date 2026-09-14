# 更新日志 / Changelog

Penrose 的所有重要变更都记录在这里。格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循[语义化版本](https://semver.org/lang/zh-CN/)。每个版本先列中文说明，再列英文说明。

All notable changes to Penrose are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/). Each version lists its changes in
Simplified Chinese first, then in English.

## [Unreleased]

## [0.1.1] - 2026-09-14

### 变更

- Windows 安装包改为 Inno Setup 向导：可选择安装目录和是否创建开始菜单文件夹，安装完成后可选择添加桌面快捷方式并启动 Penrose。默认仅为当前用户安装（`/ALLUSERS` 为所有用户安装），可在“应用和功能”中卸载。
- “检查更新”改为向 GitHub Releases 查询最新版本并指向下载页面；更新源设置已移除。

### 移除

- Velopack。应用不再自行更新，升级时直接安装新版本覆盖旧版即可。

### Changed

- The Windows installer is now an Inno Setup wizard: choose the install folder and
  whether to create a Start menu folder, then, after installing, whether to add a
  desktop shortcut and launch Penrose. It installs per user by default
  (`/ALLUSERS` installs for everyone) and uninstalls from Apps & features.
- "Check for updates" asks GitHub Releases for the latest version and points to the
  download page; the update feed setting is gone.

### Removed

- Velopack. The app no longer applies updates itself; install a new release over
  the old one instead.

## [0.1.0] - 2026-09-14

首个公开预览版。

### 新增

- 基于锁定版本的 libmpv 播放本地文件、文件夹、URL 和 `.strm` 文件，支持断点续播、播放列表、章节、光盘标题、倍速和进度条缩略图。
- 主副双字幕、外挂字幕文件、字幕编码、ASS 样式覆盖和字幕延迟；可导入 `mpv.conf`（只接受白名单内的选项）。
- 通过 Windows 高级颜色（scRGB）在普通窗口中输出 HDR；窗口无法承载时自动切换到顶层全屏 HDR10（PQ）输出；可选自动开关 Windows HDR 和匹配显示器刷新率。
- 音频输出模式（系统默认、强制立体声、家庭影院 PCM、WASAPI 独占位流直通并可回退 PCM）、功放设置向导、声道布局选择和夜间模式。
- Emby 服务器：连接测试、DPAPI 加密保存的令牌、海报墙浏览、搜索、排序、继续观看、下一集、条目页、已看 / 收藏标记和播放进度回报。
- 画中画、Xbox 手柄操作、格式徽标、英文和简体中文界面，以及会隐去敏感信息的诊断导出。
- 适用于 Windows x64、自带运行时的便携 zip 和 Velopack 安装包。

First public preview.

### Added

- Playback of local files, folders, URLs and `.strm` files on a locked libmpv
  build, with resume, playlists, chapters, disc titles, speed control and
  seek-bar thumbnails.
- Primary and secondary subtitles, external subtitle files, subtitle encoding,
  ASS style override and subtitle delay; `mpv.conf` import with a key whitelist.
- HDR in a normal window through Windows Advanced Color (scRGB), with an
  automatic top-level HDR10 (PQ) full-screen path when a window cannot carry it;
  optional Windows HDR toggling and display refresh-rate matching.
- Audio output modes (system, forced stereo, home-theater PCM, WASAPI exclusive
  bitstream with PCM fallback), an AV receiver setup wizard, channel layout
  picker and night mode.
- Emby servers: connection test, DPAPI-protected tokens, poster-wall browsing,
  search, sorting, continue watching, next up, item pages, played / favorite
  marks and playback progress reporting.
- Picture-in-picture, Xbox gamepad control, format badges, English and
  Simplified Chinese UI, and a diagnostics export that redacts secrets.
- Self-contained portable zip and Velopack installer for Windows x64.

[Unreleased]: https://github.com/zzzyang03/penrose/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/zzzyang03/penrose/releases/tag/v0.1.1
[0.1.0]: https://github.com/zzzyang03/penrose/releases/tag/v0.1.0
