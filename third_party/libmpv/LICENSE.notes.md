# libmpv license notes

The Windows libmpv builds we consume are GPL. This application is therefore
**GPL-3.0-or-later** (`LICENSE` at the repo root).

| File in package | License | Copied to |
|---|---|---|
| shinchiro `mpv-dev-x86_64-20260903-git-69e63f425a.7z` (`include/mpv/*.h`, `libmpv-2.dll`) | GPL (same as mpv) | not copied; headers stay in the extract cache |
| shinchiro player `mpv-x86_64-20260903-git-69e63f425a.7z` (`mpv.exe`, `d3dcompiler_43.dll`) | GPL + DirectX redistributable for `d3dcompiler_43.dll` | `bin/x64/` (gitignored) |

Source for shinchiro builds: https://github.com/shinchiro/mpv-winbuild-cmake

The 20260903 player archive did not ship a top-level `COPYING` file. License
follows upstream mpv (GPL) and the shinchiro build scripts.

Do not ship a non-GPL libmpv without a new ADR that changes application
license (v3 appendix A: license switch was not discussed; current baseline
stays GPL-3.0-or-later).
