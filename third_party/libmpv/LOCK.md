# libmpv lock

Binaries are **not** stored in git. Place the extracted `libmpv-2.dll` (hard-link
or copy as `mpv-2.dll`) and `d3dcompiler_43.dll` under
`third_party/libmpv/bin/x64/` on a Windows machine.
`MpvNativeLibrary` also honors `MPV_LIBRARY_PATH`.

`scripts/fetch-libmpv.ps1` downloads this snapshot via GitHub (`gh`).

## Why this snapshot (2026-09-03)

The original candidate was shinchiro **release** `mpv-0.41.0-x86_64.7z`
(SourceForge, 2025-12-25). That archive is the **player**, not libmpv, and
SourceForge returned HTTP 403 from the Windows host. shinchiro GitHub Releases
do not retain the 2025-12 tags.

Locked instead: the same vendor, generic x86_64 (**not** x64-v3), **player +
libmpv from one GitHub release / one git commit**. Official mpv 0.41.0 CI zips
are recorded as a comparison player only — they ship `mpv.exe` and no
`libmpv-2.dll`.

This is a dated snapshot, not a floating “latest”. Upgrades are a separate
commit plus a fresh verification pass.

## Baseline (embedding + bare player)

| Field | Value |
|---|---|
| Vendor | shinchiro (`mpv-winbuild-cmake`) |
| GitHub release | `20260903` |
| Product | mpv `v0.41.0-1023-g69e63f425` |
| Arch | x86_64 (generic, **not** x64-v3) |
| Commit | `69e63f425` |
| Built | 2026-09-03 00:30:53 |
| libmpv package | `mpv-dev-x86_64-20260903-git-69e63f425a.7z` |
| libmpv URL | https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20260903/mpv-dev-x86_64-20260903-git-69e63f425a.7z |
| libmpv SHA-256 | `FAC135C68A35B7639E39D72C0C365104EDBAEBDEA39A0DFDD8C36E8C8E80FAEF` |
| `libmpv-2.dll` SHA-256 | `673E6397920AB64A9C5B3A618F7F16D38854EFE72B58665F1F84E4E873B763A4` |
| player package | `mpv-x86_64-20260903-git-69e63f425a.7z` |
| player URL | https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20260903/mpv-x86_64-20260903-git-69e63f425a.7z |
| player SHA-256 | `418DBFB5FEB851CBED33D6C05D8481BA71802621BFD6EFE8974522B28D42AC97` |
| FFmpeg | `N-126390-g9fc8c785e` (`libavcodec 63.9.100`) |
| libplacebo | `v7.371.0` (`v7.360.0-120-g86bbd5d-dirty`) |
| libass | _reported at runtime via `mpv --version` / properties; not printed on the banner_ |
| License | GPL (see LICENSE.notes.md) |

`libavcodec 63.9.100` meets the P7 FEL **ABI** floor (≥ 62.35.100). That is not
an FEL pass — `enhancement-layer` still has to be proven on hardware.

## Comparison player (no libmpv; not used by the host)

| Field | Value |
|---|---|
| Vendor | mpv-player first-party CI |
| Package | `mpv-v0.41.0-x86_64-pc-windows-msvc.zip` |
| URL | https://github.com/mpv-player/mpv/releases/download/v0.41.0/mpv-v0.41.0-x86_64-pc-windows-msvc.zip |
| SHA-256 | `4E197F729F5071C6772F35FFFD96E0F36E3E8A044BD9479B136BB09B7C6A80FF` |
| Contents | `mpv.exe`, `vulkan-1.dll` — **no** `libmpv-2.dll` |

## FEL experiment (conditional, does not block baseline)

| Field | Value |
|---|---|
| Requirement | layered-video / `hvcE`, libavcodec ABI ≥ 62.35.100 (FFmpeg 9.0 branch) |
| This snapshot | ABI **present** (`63.9.100`); image correctness / hwdec path **not** verified |
| Status | **not locked as FEL-on** — hidden until verified |
| Commit | same as baseline until a dedicated FEL package is chosen |

If verification rejects FEL, keep `enhancement-layer=no`.

## Optional x86_64-v3 (comparison only, not the product lock)

Same vendor, date, and git commit as the baseline. Reference CPU: Core Ultra 9 285H.
`ffmpeg -benchmark` on the sample clips (libavcodec 63.9.100) did **not** show a
≥10% single-thread gain vs generic. Keep `bin/x64` as the v1 lock. Sidecar
extract: `third_party/libmpv/bin/x64-v3/` (gitignored).

| Field | Value |
|---|---|
| libmpv package | `mpv-dev-x86_64-v3-20260903-git-69e63f425a.7z` |
| libmpv SHA-256 | `8594D40336EA038C9289E97E0A47110C850C762CF9954186F2AC1C56809D4E31` |
| `libmpv-2.dll` SHA-256 | `25D753D74D817C8BD74E9C687696065DF7FF09A1928C11A2AB08B596F25A0F8F` |
| player package | `mpv-x86_64-v3-20260903-git-69e63f425a.7z` |
| player SHA-256 | `8786587181584F49E79217913043C5969BEAAE1EF202D6B6D4BC9EE708FDC75E` |
| ffmpeg package | `ffmpeg-x86_64-v3-git-9fc8c785e.7z` |
| ffmpeg SHA-256 | `DD4D9E6B34890D27C715F3E9FC67B69AEB110F26F53CA371B4DB5A7A2ED3A2B5` |

Do not point the product host at this tree.

## Local layout

```
third_party/libmpv/
  LOCK.md
  LICENSE.notes.md
  bin/x64/          gitignored
    libmpv-2.dll    (canonical export name)
    mpv-2.dll       (hard-link of libmpv-2.dll; DllImport name)
    d3dcompiler_43.dll
    mpv.exe         (bare player, same commit)
    mpv.com
```

`vulkan-1.dll` is a hard import on this build. This host provides it via the
GPU driver (`%SystemRoot%\System32\vulkan-1.dll`). Do not copy a random
loader into git.

The product host loads `mpv-2.dll` from this directory (or `MPV_LIBRARY_PATH`).
Do not mix vendors across builds.
