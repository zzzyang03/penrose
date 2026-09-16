# 更新日志 / Changelog

Penrose 的所有重要变更都记录在这里。格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循[语义化版本](https://semver.org/lang/zh-CN/)。每个版本先列中文说明，再列英文说明。

All notable changes to Penrose are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/). Each version lists its changes in
Simplified Chinese first, then in English.

## [Unreleased]

### 修复

- 局域网 Emby/Jellyfin 的 strm（Path 指向 OpenList 等外链）改为直连该 URL 并跟随 302，不再走服务器 DirectStream 中继；HTTP 探测从 2 MiB / 2 秒提高到 10 MiB / 6 秒。此前 4K 杜比视界 / DDP Atmos 片源经常首次无声、HDR 元数据来不及读到，退出后再播也无法再次点亮 Windows HDR。
- 杜比视界（含 Profile 5）也视为 HDR 片源；等到 `video-params` / 音轨就绪后再开关 Windows HDR，若系统 HDR 已经打开则仍刷新 scRGB 管线。迟到出现的音轨会自动选中。

### Fixed

- LAN Emby/Jellyfin `.strm` items whose Path is an OpenList (or other off-origin)
  URL now play that URL so mpv follows the 302 itself, instead of tunnelling
  through the server DirectStream route. The HTTP lavf probe is 10 MiB / 6 s
  (was 2 MiB / 2 s). 4K Dolby Vision / DDP Atmos files often started silent and
  missed HDR metadata; a second play then could not turn Windows HDR back on.
- Dolby Vision (including profile 5) counts as an HDR source. Windows HDR is
  applied after `video-params` / tracks are known, and the scRGB pipeline is
  still refreshed when Windows HDR is already on. A late audio track is
  selected automatically.

## [0.2.0] - 2026-09-15

### 新增

- 播放信息面板重新组织为 5 个区块：播放类型（本地 / strm / 服务器直连 / 服务器转码等）、媒体源（封装格式、大小、完整地址）、视频（编码、动态范围、分辨率、帧率、码率）、音频（编码、声道、采样率、码率）、输出。视频和音频码率、帧率在面板打开时以 1 Hz 实时刷新。
- 设置页“音频输出”和音轨菜单里新增“音频直通”开关。打开后 AC3 / E-AC3 / DTS / TrueHD 以位流交给功放解码（WASAPI 独占），其他编码仍由播放器按所选输出模式解码；名单内编码未能直通时回退 PCM 并提示。
- 音频延迟调节：播放时按 `Ctrl`+`-` / `Ctrl`+`=`，或在音轨菜单里让声音提前或推后 50 毫秒，修正音画不同步。只对当前文件生效，打开下一个文件时自动归零。
- 设置页“画面”分区新增“硬件解码”开关，默认开启。遇到花屏、绿屏、黑屏或播放崩溃时可以关闭，改用软件解码；切换后当前播放立即生效，设置会保存。

### 变更

- 输出模式只保留三档 PCM：系统默认、强制立体声、家庭影院 PCM。原来的“位流”模式并入“音频直通”开关，旧设置里的“位流”会自动迁移为“家庭影院 PCM + 音频直通”。
- 音频直通开启时夜间模式不可用；直通打开但当前音轨仍由播放器解码时，声道下混选择照常可用。
- 首页“影视服务器”按钮换用新的 NAS 图标。

### 修复

- 窗口失去焦点时不再把 HDR（scRGB）输出切换为 SDR。此前每次切换窗口都会重建交换链，造成短暂黑屏和卡顿；现在窗口在后台时继续以 HDR 播放。

### Added

- The I-key playback info overlay is reorganized into 5 labeled sections:
  playback kind (local / .strm direct / .strm relay / server direct play / server
  transcode), media source (container, size, full URI), video (codec, dynamic
  range, resolution, fps, bitrate), audio (codec profile, channels, sample rate,
  bitrate) and output (HW decoder, swapchain, peak, refresh, audio device).
  Video and audio bitrate and fps refresh at 1 Hz while the overlay is open.
- An "Audio passthrough" toggle in the settings page (Audio output) and in the
  audio track menu. When on, AC3 / E-AC3 / DTS / TrueHD are sent as a bitstream
  for the receiver to decode (WASAPI exclusive); other codecs are still decoded
  by the player in the selected output mode. A listed codec that fails to pass
  through falls back to PCM with a prompt.
- Audio delay: press `Ctrl`+`-` / `Ctrl`+`=` or use the audio track menu to play
  the sound 50 ms earlier or later and fix audio/video sync. It applies to the
  current file only and resets when the next file opens.
- A "Hardware decoding" toggle in the settings page (Video), on by default. Turn it
  off to decode in software if you see corrupted, green or black frames or
  playback crashes; the change applies to the current playback and is saved.

### Changed

- Output modes are now the three PCM layouts: system default, forced stereo and
  home-theater PCM. The former "Bitstream" mode became the passthrough toggle;
  a saved "Bitstream" setting migrates to home-theater PCM with passthrough on.
- Night mode is unavailable while passthrough is on; the channel downmix picker
  stays available for tracks the player still decodes.
- The "Media servers" button on the home page has a new NAS icon.

### Fixed

- Losing window focus no longer switches HDR (scRGB) output to SDR. Every focus
  change used to rebuild the swap chain, causing a brief black screen and stutter;
  playback now stays in HDR while the window is in the background.

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

[Unreleased]: https://github.com/zzzyang03/penrose/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/zzzyang03/penrose/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/zzzyang03/penrose/releases/tag/v0.1.1
[0.1.0]: https://github.com/zzzyang03/penrose/releases/tag/v0.1.0
