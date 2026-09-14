# 安全策略 / Security policy

简体中文 · [English](#english)

## 支持的版本

Penrose 目前是早期预览版，只有最新发布的版本会获得安全修复。

## 报告漏洞

请**不要**为安全问题开公开的 issue，而是在 GitHub 上私下报告：在本仓库点 **Security → Report a vulnerability**，或者直接打开[这个链接](https://github.com/zzzyang03/penrose/security/advisories/new)。

请写明 Penrose 版本、攻击者能做到什么，以及复现步骤。不要附上真实的服务器地址、密码和令牌。

## 范围

例如以下情况属于报告范围：

- 媒体服务器的访问令牌或密码通过日志、诊断导出、设置文件或崩溃报告泄露
- 通过精心构造的媒体文件、播放列表、`.strm` 文件、导入的 `mpv.conf`，或 Penrose 处理的媒体服务器响应，执行代码或访问文件
- Penrose 查找和加载 libmpv 的方式，或更新流程中存在的弱点

libmpv、FFmpeg 或其他随附组件自身的漏洞，也请同时报告给相应项目。上游发布修复后，Penrose 会随之更新锁定的 libmpv 版本。

---

## English

### Supported versions

Penrose is an early preview. Only the latest release receives security fixes.

### Reporting a vulnerability

Please **do not** open a public issue for security problems. Report them
privately on GitHub instead: **Security → Report a vulnerability** on this
repository, or use
[this link](https://github.com/zzzyang03/penrose/security/advisories/new).

Include the Penrose version, what an attacker could achieve, and steps to
reproduce. Leave out real server addresses, passwords and tokens.

### Scope

Examples of what is in scope:

- Leaks of media-server access tokens or passwords through logs, diagnostics
  exports, settings files or crash reports
- Code execution or file access through crafted media files, playlists, `.strm`
  files, imported `mpv.conf` files or media-server responses handled by Penrose
- Weaknesses in how Penrose locates and loads libmpv, or in the update path

Vulnerabilities inside libmpv, FFmpeg or other bundled components should also be
reported to those projects. Penrose moves its locked libmpv build once an
upstream fix is available.
