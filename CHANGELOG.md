# Changelog

All notable changes to Penrose are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-09-14

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

[0.1.0]: https://github.com/zzzyang03/penrose/releases/tag/v0.1.0
