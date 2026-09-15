<p align="center">
  <img src="src/Penrose.App.WinUI/Assets/AppIcon.png" alt="Penrose 图标" width="112">
</p>

<h1 align="center">Penrose</h1>

<p align="center">
  <b>基于 libmpv 的 Windows 媒体播放器：窗口内也能正确输出 HDR，内置 Emby 客户端。</b>
</p>

<p align="center">
  <a href="https://github.com/zzzyang03/penrose/releases/latest"><img src="https://img.shields.io/github/v/release/zzzyang03/penrose?label=release" alt="最新版本"></a>
  <a href="https://github.com/zzzyang03/penrose/actions/workflows/ci.yml"><img src="https://github.com/zzzyang03/penrose/actions/workflows/ci.yml/badge.svg" alt="CI 状态"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--or--later-blue" alt="许可证：GPL-3.0-or-later"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2011%20x64-0078D4" alt="平台：Windows 11 x64">
</p>

<!-- v0.1.0 的 Release 说明链接到 #简体中文，保留这个锚点 -->
<p align="center">
  <a name="简体中文"></a>简体中文 · <a href="#english">English</a>
</p>

> [!NOTE]
> **早期预览版。** Penrose 目前只在一套 Windows 11 x64 环境上开发和测试过，难免有粗糙之处，遇到问题欢迎[反馈](https://github.com/zzzyang03/penrose/issues/new/choose)。

## 下载

在 [**Releases**](https://github.com/zzzyang03/penrose/releases/latest) 下载最新版本：

| 文件 | 适用情况 |
|---|---|
| `Penrose-<版本>-win-x64-Setup.exe` | 常规安装：向导中可选择安装目录、开始菜单文件夹和桌面快捷方式，不需要管理员权限（默认仅当前用户，加 `/ALLUSERS` 参数可为所有用户安装） |
| `Penrose-<版本>-win-x64-portable.zip` | 解压到任意位置，直接运行 `Penrose.exe` |

两种包都自带运行时，无需另装 .NET 或 Windows App SDK。目前还没有代码签名，首次运行如果 SmartScreen 拦截，点 **更多信息 → 仍要运行**。

**系统要求**

- Windows 11 x64（Windows 10 也许能用，但未测试）
- 较新的 NVIDIA / AMD / Intel 显卡驱动（播放内核依赖驱动安装的 Vulkan 加载器 `vulkan-1.dll`）
- 需要 HDR 时：支持 HDR 的显示器，并且 Windows 可以打开 HDR

## 功能

**播放**

- 本地文件、文件夹、URL、`.strm` 文件与拖放，断点续播
- 播放列表、章节、光盘标题选择（ISO / BDMV / DVD）、倍速和进度条缩略图
- 主副双字幕、外挂字幕文件、字幕编码、ASS 样式覆盖和字幕延迟
- 可导入现有的 `mpv.conf`；不支持的选项会明确提示原因，而不是悄悄忽略
- 键盘、鼠标和 Xbox 手柄操作

**画面与 HDR**

- 在 WinUI 合成表面上通过 Windows 高级颜色（scRGB）在窗口内输出 HDR
- 窗口无法承载 HDR 信号时，自动切换到顶层全屏并以 HDR10（PQ）输出
- 可选：播放 HDR 片源时打开 Windows HDR、匹配显示器刷新率，播完后都会还原
- 画中画、画质预设，以及 Dolby Vision、HDR10+、Dolby Atmos、DTS:X 格式徽标

**音频**

- 输出模式：系统默认、强制立体声、家庭影院 PCM（5.1 / 7.1）；独立的“音频直通”开关把 AC3 / E-AC3 / DTS / TrueHD 以 WASAPI 独占位流交给功放解码（失败时自动回退 PCM 并提示）
- 通过 HDMI 连接功放和回音壁的设置向导
- 声道布局（下混）选择与夜间模式（动态范围压缩）

**Emby**

- 添加服务器并测试连接；访问令牌用 Windows DPAPI 加密保存，不会写入设置
- 海报墙浏览（从媒体库一直到季和集）、服务端搜索、排序、继续观看和下一集
- 条目页：背景图、评分、版本 / 音轨 / 字幕选择、演职员和相似推荐；可标记已看或收藏
- 直接播放，并回报开始 / 进度 / 停止，续播位置会同步到服务器

Jellyfin 支持仍在开发中：已能解析播放信息，但应用暂时还不能连接 Jellyfin 服务器。

**界面**

- WinUI 3，简体中文 / English
- 诊断导出会自动隐去日志中的令牌和密码

## 快捷键

| 按键 | 功能 |
|---|---|
| `空格` | 播放 / 暂停 |
| `←` / `→` | 后退 / 前进 5 秒 |
| `Home` / `End` | 跳到开头 / 结尾 |
| `↑` / `↓`、鼠标滚轮 | 音量 |
| `M` | 静音 |
| `Page Up` / `Page Down` | 上一个 / 下一个 |
| `[` / `]` | 减速 / 加速（每次 0.25×） |
| `Z` / `X` | 字幕延迟 −0.1 秒 / +0.1 秒 |
| `A` / `S` | 切换音轨 / 字幕 |
| `F`、双击 | 全屏 |
| `T` | 顶层全屏（HDR10 输出） |
| `P` | 画中画 |
| `I` | 播放信息 |
| `N` | 夜间模式 |
| `O` / `U` | 打开文件 / 打开 URL |
| `E` | 导出诊断信息 |
| `Ctrl` + `,` | 设置 |
| `Esc` | 退出全屏或关闭当前页面 |

也支持媒体键；Xbox 手柄 **A** 播放 / 暂停，**LB** / **RB** 上一个 / 下一个。

## 从源码构建

准备：

- Windows 11 x64
- [.NET SDK 10.0.4xx](https://dotnet.microsoft.com/download/dotnet/10.0)（由 `global.json` 固定）
- [7-Zip](https://www.7-zip.org/)，用于解压 libmpv 压缩包（较新的 Windows 11 可以不装，会改用系统自带的 `tar.exe`）
- [Windows App Runtime 2.x](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)，运行 Debug 版时需要（发布包已自带）

```powershell
git clone https://github.com/zzzyang03/penrose.git
cd penrose

# 下载锁定版本的 libmpv 到 third_party\libmpv\bin\x64（校验 SHA-256）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\fetch-libmpv.ps1

# 构建并启动应用（只在源码有变化时重新构建；加 --build 强制构建）
scripts\run-app.cmd

# 运行测试
dotnet test Penrose.sln
```

打包发布版（便携 zip 和安装包输出到 `artifacts\`；安装包需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)，可用 `winget install JRSoftware.InnoSetup` 安装）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pack.ps1
```

libmpv 二进制文件不入库，具体版本、下载地址和哈希记录在 [`third_party/libmpv/LOCK.md`](third_party/libmpv/LOCK.md)。

## 项目结构

| 路径 | 内容 |
|---|---|
| `src/Penrose.Core` | 播放状态、策略、设置和界面文字（可移植的 `net10.0`） |
| `src/Penrose.Interop.LibMpv` | libmpv 的 P/Invoke 绑定与库加载 |
| `src/Penrose.Playback.Mpv` | 基于 libmpv 的播放引擎 |
| `src/Penrose.VideoSurface.*` | 视频表面接口，以及 D3D11 / WinUI 合成实现 |
| `src/Penrose.Sources.Emby`、`src/Penrose.Sources.Jellyfin` | 媒体服务器客户端 |
| `src/Penrose.Persistence` | 保存续播位置的 SQLite 存储 |
| `src/Penrose.Diagnostics` | 日志脱敏与诊断导出 |
| `src/Penrose.App.WinUI` | Windows 应用（生成 `Penrose.exe`） |
| `tests/` | 各个库的 xUnit 测试 |
| `scripts/` | `fetch-libmpv`、`run-app`、`pack` |
| `third_party/libmpv/` | libmpv 锁定文件与许可证说明（不含二进制） |

`Penrose.sln` 包含可移植的库和测试，`Penrose.Windows.sln` 包含 Windows 应用。

## 反馈与贡献

欢迎在 [Issues](https://github.com/zzzyang03/penrose/issues/new/choose) 提交问题和功能建议。播放相关的问题，请在播放器里按 **E** 导出诊断文件并附上。安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。

欢迎提交 Pull Request。比小修复更大的改动，请先开 issue 商量做法。CI 会为每个 Pull Request 运行测试并构建 Windows 应用。Issue 和 Pull Request 用中文或英文都可以。

## 许可证

Copyright (C) 2026 Zhehao Yang

Penrose 是自由软件，以 [GNU 通用公共许可证第 3 版或更高版本](LICENSE)（GPL-3.0-or-later）发布。它链接了含 GPL 组件的 libmpv。随附的第三方软件及其许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)（英文）。

Penrose 是独立项目，与 Emby、Dolby Laboratories、DTS, Inc.、HDR10+ Technologies, LLC 均无关联，也未获其认可。相关名称与标志归各自所有者所有，应用中仅用于标识内容格式和服务器类型。

---

## English

<p align="center">
  <b>A Windows media player built on libmpv, with HDR that works in a normal window and a built-in Emby client.</b>
</p>

> [!NOTE]
> **Early preview.** Penrose has been developed and tested on a single Windows 11 x64 setup.
> Expect rough edges, and please [report](https://github.com/zzzyang03/penrose/issues/new/choose) what breaks.

### Download

Get the latest version from [**Releases**](https://github.com/zzzyang03/penrose/releases/latest):

| File | Choose it if |
|---|---|
| `Penrose-<version>-win-x64-Setup.exe` | you want a regular install: the wizard lets you pick the folder, a Start menu folder and a desktop shortcut, and no administrator rights are needed (per user by default; `/ALLUSERS` installs for everyone) |
| `Penrose-<version>-win-x64-portable.zip` | you want to unzip anywhere and run `Penrose.exe` |

Both builds are self-contained, so .NET and the Windows App SDK do not need to be installed.
They are not code-signed yet: if SmartScreen warns on first launch, choose **More info → Run anyway**.

**System requirements**

- Windows 11 x64 (Windows 10 may work but is untested)
- A current NVIDIA, AMD or Intel GPU driver (the playback engine needs the Vulkan loader, `vulkan-1.dll`, that these drivers install)
- For HDR: an HDR-capable display with Windows HDR available

### Features

**Playback**

- Local files, folders, URLs, `.strm` files and drag and drop, with resume where you left off
- Playlists, chapters, disc titles (ISO / BDMV / DVD title picker), speed control and seek-bar thumbnails
- Primary and secondary subtitles, external subtitle files, subtitle encoding, ASS style override and subtitle delay
- Import an existing `mpv.conf`; unsupported options are rejected with a reason instead of being silently ignored
- Keyboard, mouse and Xbox controller input

**Video and HDR**

- HDR in a normal window through Windows Advanced Color (scRGB) on a WinUI composition surface
- Automatic switch to top-level full screen with HDR10 (PQ) output when a window cannot carry the signal
- Optional: turn on Windows HDR for HDR files and match the display refresh rate, both restored afterwards
- Picture-in-picture, picture-quality presets, and format badges for Dolby Vision, HDR10+, Dolby Atmos and DTS:X

**Audio**

- Output modes: system default, forced stereo, home-theater PCM (5.1 / 7.1); a separate audio passthrough toggle sends AC3 / E-AC3 / DTS / TrueHD as a WASAPI exclusive bitstream for the receiver to decode, with automatic PCM fallback and a prompt
- Setup wizard for AV receivers and soundbars over HDMI
- Channel layout (downmix) picker and night mode (dynamic range compression)

**Emby**

- Add servers with a connection test; access tokens are protected with Windows DPAPI and never written to settings
- Poster-wall browsing from libraries down to seasons and episodes, server-side search, sorting, continue watching and next up
- Item pages with backdrop, ratings, version / audio / subtitle pickers, cast and similar titles; mark items as played or favorite
- Direct play with start / progress / stop reporting, so resume points show up on the server

Jellyfin support is in progress: playback-info parsing exists, but the app cannot connect to a Jellyfin server yet.

**Interface**

- WinUI 3, Simplified Chinese and English
- Diagnostics export that redacts tokens and passwords from the logs

### Keyboard and mouse

| Input | Action |
|---|---|
| `Space` | Play / pause |
| `←` / `→` | Seek back / forward 5 s |
| `Home` / `End` | Jump to start / end |
| `↑` / `↓`, mouse wheel | Volume |
| `M` | Mute |
| `Page Up` / `Page Down` | Previous / next item |
| `[` / `]` | Slower / faster (0.25× steps) |
| `Z` / `X` | Subtitle delay −0.1 s / +0.1 s |
| `A` / `S` | Cycle audio / subtitle track |
| `F`, double-click | Full screen |
| `T` | Top-level full screen (HDR10 output) |
| `P` | Picture-in-picture |
| `I` | Playback info |
| `N` | Night mode |
| `O` / `U` | Open file / open URL |
| `E` | Export diagnostics |
| `Ctrl` + `,` | Settings |
| `Esc` | Leave full screen or close the current page |

Media keys work as well, and an Xbox controller maps **A** to play / pause and **LB** / **RB** to previous / next.

### Build from source

Prerequisites:

- Windows 11 x64
- [.NET SDK 10.0.4xx](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json`)
- [7-Zip](https://www.7-zip.org/) to unpack the libmpv archives (optional on current Windows 11, whose built-in `tar.exe` is used as a fallback)
- [Windows App Runtime 2.x](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads), needed to run Debug builds (release packages bundle it)

```powershell
git clone https://github.com/zzzyang03/penrose.git
cd penrose

# Download the locked libmpv build into third_party\libmpv\bin\x64 (SHA-256 verified)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\fetch-libmpv.ps1

# Build and start the app (rebuilds only when sources changed; pass --build to force)
scripts\run-app.cmd

# Run the tests
dotnet test Penrose.sln
```

To produce the release packages (portable zip and installer under `artifacts\`; the installer
needs [Inno Setup 6](https://jrsoftware.org/isinfo.php), `winget install JRSoftware.InnoSetup`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pack.ps1
```

libmpv binaries are never committed. [`third_party/libmpv/LOCK.md`](third_party/libmpv/LOCK.md) records the exact build, download URLs and hashes.

### Project layout

| Path | Contents |
|---|---|
| `src/Penrose.Core` | Playback state, policies, settings and UI strings (portable `net10.0`) |
| `src/Penrose.Interop.LibMpv` | libmpv P/Invoke bindings and library loading |
| `src/Penrose.Playback.Mpv` | Playback engine on top of libmpv |
| `src/Penrose.VideoSurface.*` | Video surface contracts and the D3D11 / WinUI composition implementation |
| `src/Penrose.Sources.Emby`, `src/Penrose.Sources.Jellyfin` | Media server clients |
| `src/Penrose.Persistence` | SQLite store for resume positions |
| `src/Penrose.Diagnostics` | Log redaction and diagnostics export |
| `src/Penrose.App.WinUI` | The Windows app (builds `Penrose.exe`) |
| `tests/` | xUnit tests for the libraries |
| `scripts/` | `fetch-libmpv`, `run-app`, `pack` |
| `third_party/libmpv/` | libmpv lock file and license notes (no binaries) |

`Penrose.sln` contains the portable libraries and tests; `Penrose.Windows.sln` contains the Windows app.

### Feedback and contributions

Bug reports and feature requests are welcome in [Issues](https://github.com/zzzyang03/penrose/issues/new/choose).
For playback problems, press **E** in the player to export diagnostics and attach the file.
Please report security issues privately as described in [SECURITY.md](SECURITY.md).

Pull requests are welcome. For anything bigger than a small fix, please open an issue first to agree on the approach.
CI runs the tests and builds the Windows app for every pull request. Issues and pull requests may be written in Chinese or English.

### License

Copyright (C) 2026 Zhehao Yang

Penrose is free software released under the [GNU General Public License v3.0 or later](LICENSE).
It links libmpv, which is built with GPL components. Bundled third-party software and its licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Penrose is an independent project and is not affiliated with or endorsed by Emby, Dolby Laboratories, DTS, Inc.
or HDR10+ Technologies, LLC. Their names and logos are trademarks of their respective owners and appear in the app
only to identify content formats and server types.
