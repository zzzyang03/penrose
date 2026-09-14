# libmpv license notes

The Windows libmpv builds we consume are GPL. Penrose is therefore
**GPL-3.0-or-later** (`LICENSE` at the repo root). Every component bundled in
release packages is listed in `THIRD-PARTY-NOTICES.md`.

| File in package | License | Used as |
|---|---|---|
| shinchiro `mpv-dev-x86_64-20260903-git-69e63f425a.7z` (`include/mpv/*.h`, `libmpv-2.dll`) | GPL (same as mpv) | `bin/x64/libmpv-2.dll` + `mpv-2.dll` (gitignored); only `mpv-2.dll` ships |
| shinchiro player `mpv-x86_64-20260903-git-69e63f425a.7z` (`mpv.exe`) | GPL | `bin/x64/mpv.exe` (gitignored); local comparisons only, never shipped |

Source for shinchiro builds: https://github.com/shinchiro/mpv-winbuild-cmake

The 20260903 player archive did not ship a top-level `COPYING` file. License
follows upstream mpv (GPL) and the shinchiro build scripts.

The player archive also contains `d3dcompiler_43.dll`, a DirectX SDK
redistributable. It is neither used nor shipped (see `LOCK.md`).

Penrose can only move off the GPL after switching to a non-GPL (LGPL) libmpv
build; until then it stays GPL-3.0-or-later.
