# Security policy

## Supported versions

Penrose is an early preview. Only the latest release receives security fixes.

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Report them
privately on GitHub instead: **Security → Report a vulnerability** on this
repository, or use
[this link](https://github.com/zzzyang03/penrose/security/advisories/new).

Include the Penrose version, what an attacker could achieve, and steps to
reproduce. Leave out real server addresses, passwords and tokens.

## Scope

Examples of what is in scope:

- Leaks of media-server access tokens or passwords through logs, diagnostics
  exports, settings files or crash reports
- Code execution or file access through crafted media files, playlists, `.strm`
  files, imported `mpv.conf` files or media-server responses handled by Penrose
- Weaknesses in how Penrose locates and loads libmpv, or in the update path

Vulnerabilities inside libmpv, FFmpeg or other bundled components should also be
reported to those projects. Penrose moves its locked libmpv build once an
upstream fix is available.
