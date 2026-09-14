# Third-party notices

Penrose is licensed under the GNU General Public License, version 3 or later
(see `LICENSE`). Release packages also contain the third-party components
listed below, each under its own license.

## libmpv

`mpv-2.dll` is `libmpv-2.dll` from the shinchiro Windows build of mpv,
redistributed unmodified.

| | |
|---|---|
| Build | [shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake), release [`20260903`](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20260903), package `mpv-dev-x86_64-20260903-git-69e63f425a.7z` |
| SHA-256 of `libmpv-2.dll` | `673E6397920AB64A9C5B3A618F7F16D38854EFE72B58665F1F84E4E873B763A4` |
| [mpv](https://github.com/mpv-player/mpv) | `v0.41.0-1023-g69e63f425`, GPL-2.0-or-later (built with GPL features) |
| [FFmpeg](https://ffmpeg.org/) | `N-126390-g9fc8c785e`, GPL build |
| [libplacebo](https://code.videolan.org/videolan/libplacebo) | `v7.371.0`, LGPL-2.1-or-later |
| [libass](https://github.com/libass/libass) | ISC |

The DLL statically links many more libraries, among them x264, x265, libvpx,
libaom, dav1d, zimg, libbluray, libdvdnav, Rubber Band, FreeType, HarfBuzz,
FriBidi, Little CMS, shaderc, glslang, SPIRV-Cross, LuaJIT, uchardet, libarchive,
libxml2, OpenSSL, libssh, SRT and zlib, each under its own license (GPL, LGPL,
BSD, MIT, Apache-2.0, ISC and similar). The build recipe at the release tag
above lists every component and how it was configured.

### Corresponding source

- mpv: https://github.com/mpv-player/mpv/tree/69e63f425
- FFmpeg: https://github.com/FFmpeg/FFmpeg/tree/9fc8c785e
- Build recipe: https://github.com/shinchiro/mpv-winbuild-cmake/tree/20260903

If you cannot obtain the complete corresponding source for this binary from
these locations, open an issue in the Penrose repository and it will be provided.

## .NET and Windows App SDK

| Component | Version | License |
|---|---|---|
| .NET runtime (bundled by the self-contained build) | 10.0.11 | MIT, https://github.com/dotnet/runtime |
| Windows App SDK including WinUI 3 (`Microsoft.WindowsAppSDK` and its component packages) | 2.4.0 | Microsoft Software License Terms for the Windows App SDK, https://github.com/microsoft/windowsappsdk |
| Windows ML runtime (`Microsoft.Windows.AI.MachineLearning`, pulled in by the Windows App SDK) | 2.1.74 | Microsoft Software License Terms |
| WebView2 loader (`Microsoft.Web.WebView2`) | 1.0.3719.77 | BSD-style license, Copyright (C) Microsoft Corporation |
| `Microsoft.Data.Sqlite` | 10.0.11 | MIT |
| `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `System.Security.Cryptography.ProtectedData` | 10.0.11 | MIT |
| `System.Numerics.Tensors` | 9.0.0 | MIT |

The Microsoft license texts ship inside the corresponding NuGet packages.

## Other libraries

| Component | Version | License |
|---|---|---|
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) with `e_sqlite3` | 2.1.12 | Apache-2.0; SQLite itself is in the public domain |
| [Serilog](https://github.com/serilog/serilog) | 4.4.0 | Apache-2.0 |
| [Serilog.Sinks.File](https://github.com/serilog/serilog-sinks-file) | 7.0.0 | Apache-2.0 |

## Trademarks

Dolby, Dolby Vision and Dolby Atmos are trademarks of Dolby Laboratories. DTS
and DTS:X are trademarks of DTS, Inc. HDR10+ is a trademark of HDR10+
Technologies, LLC. Emby is a trademark of its owner. These names and logos
appear in Penrose only to identify content formats and server types. Penrose is
an independent project and is not affiliated with or endorsed by any of these
companies.
