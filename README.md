<p align="center">
  <img src="design/penrose-icon-navy.png" alt="Penrose" width="128">
</p>

<h1 align="center">Penrose</h1>

<p align="center">
  <b>A Windows-first media player built on libmpv</b><br>
  HDR-correct rendering · Emby library browsing · bilingual UI
</p>

<p align="center">
  <img src="https://img.shields.io/badge/license-GPL--3.0-blue.svg" alt="License: GPL-3.0">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/platform-Windows%2011-0078D4" alt="Platform: Windows 11">
  <img src="https://img.shields.io/badge/version-0.1.0-orange" alt="Version 0.1.0">
</p>

<p align="center">
  English · <a href="#简体中文">简体中文</a>
</p>

---

## Features

**Playback** — powered by a locked libmpv snapshot

- Local files, folders, URLs and drag-and-drop; disc-title picker
- Resume positions, playlists, chapters, playback speed, seek thumbnails
- Dual subtitles, external subtitle loading, encoding / ASS-style override, per-track delay
- `mpv.conf` import for existing mpv users

**Video & HDR**

- Windowed scRGB (Advanced Color) rendering on a WinUI composition surface
- Top-level PQ path for HDR10 output where a window cannot carry it
- Refresh-rate matching, PiP, per-display restore on multi-monitor setups
- Format badges for Dolby Vision / Atmos, DTS:X, HDR10+ and channel layout, with a downmix picker

**Audio**

- Output policies: system-compatible, forced stereo, home-theater PCM, WASAPI-exclusive bitstream passthrough
- Amp / soundbar wizard for HDMI AVR setups, with automatic PCM fallback
- Night mode (dynamic-range compression)

**Emby**

- Add a server with a test-connection button; credentials stay in DPAPI, never in settings JSON
- Poster-wall browsing (libraries → titles → seasons → episodes), server-side search, incremental paging
- Item pages modeled on Emby's own: backdrop hero, ratings, version / audio / subtitle pickers, next up, cast, similar titles
- DirectPlay with `X-Emby-Token`, session start / progress / stop reporting — resume points show up server-side
- Jellyfin support is in progress (`MediaPlayer.Sources.Jellyfin`)

**Interface** — WinUI 3, zh-CN and en UI

## Status

**v0.1.0 — early preview.** Windows 11 x64 is the only verified host today.
The shared libraries are portable `net10.0`; a macOS host is planned but not
started. Expect rough edges; diagnostics export is built in.

## Requirements

- Windows 11 x64
- [.NET SDK 10.0.4xx](https://dot.net/) (`global.json` pins the band)
- Locked libmpv build — fetched by script, not stored in git
  (`third_party/libmpv/LOCK.md` records the vendor, commit and SHA-256)

## Build & run

```powershell
# fetch the locked libmpv native binaries into third_party/libmpv/bin
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\fetch-libmpv.ps1

dotnet restore MediaPlayer.Windows.sln
dotnet run --project src\MediaPlayer.App.WinUI\MediaPlayer.App.WinUI.csproj
```

Run the portable-core test suite:

```powershell
dotnet test MediaPlayer.sln
```

For a portable publish without a build step, run `scripts\pack.ps1` once and
launch `artifacts\publish\win-x64\MediaPlayer.App.WinUI.exe`.

## Project layout

```
src/          shared core, libmpv interop, playback, video surfaces,
              Emby/Jellyfin sources, diagnostics, persistence, WinUI 3 host
tests/        unit and contract tests (platform-agnostic where possible)
scripts/      fetch-libmpv, pack, run helpers
design/       icon sources and generator scripts
third_party/  libmpv lock metadata (no binaries)
```

## License

[GPL-3.0-or-later](LICENSE) — follows the GPL license of libmpv.

Format and server icons under `src/MediaPlayer.App.WinUI/Assets/` are
trademarks of their respective owners and are used for identification only.

---

## 简体中文

<p align="center">
  <b>基于 libmpv 的 Windows 优先媒体播放器</b><br>
  HDR 正确渲染 · Emby 媒体库浏览 · 中英双语界面
</p>

**当前版本 v0.1.0（早期预览）**，仅在 Windows 11 x64 验证。共享核心库为可移植 `net10.0`，macOS 宿主在规划中。

### 功能一览

- **播放**：本地文件 / 文件夹 / URL / 拖放；断点续播、播放列表、章节、倍速、进度缩略图；双字幕、外挂字幕、编码与 ASS 样式覆盖、字幕延迟；可导入 `mpv.conf`
- **画面**：窗口 scRGB（高级颜色）渲染；顶层 PQ 通道输出 HDR10；刷新率匹配、画中画、多屏位置恢复；DV / Atmos / DTS:X / HDR10+ 格式徽标与声道布局下混选择
- **音频**：系统兼容 / 强制立体声 / 家庭影院 PCM / WASAPI 独占位流直通；HDMI 功放向导，失败自动回退 PCM；夜间模式（动态范围压缩）
- **Emby**：测试连接、账号凭据存 DPAPI；海报墙浏览、服务端搜索、分页加载；条目页含版本 / 音轨 / 字幕选择、Next Up、演职员、相似推荐；DirectPlay + 播放进度回报
- **界面**：WinUI 3，中文 / English

### 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\fetch-libmpv.ps1
dotnet restore MediaPlayer.Windows.sln
dotnet run --project src\MediaPlayer.App.WinUI\MediaPlayer.App.WinUI.csproj
```

需要 .NET SDK 10.0.4xx；libmpv 二进制不入库，由脚本按 `third_party/libmpv/LOCK.md` 的锁定快照拉取。

### 许可证

[GPL-3.0-or-later](LICENSE)（跟随 libmpv 的 GPL 许可）。
