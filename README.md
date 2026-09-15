<div align="center">

<img width="120" height="120" src="Odeon/Assets/StoreLogo.scale-400.png" alt="Odeon app icon — a lightweight, open-source media player for Windows 10 and 11">

# Odeon

### Lightweight, Open-Source Media Player for Windows

**A modern, distraction-free video and audio player for Windows 10/11, powered by libmpv.**
<br>
*The privacy-focused, no-telemetry VLC alternative — 4K/HDR hardware-accelerated playback with native Windows acrylic materials.*

<br>

<img src="https://img.shields.io/badge/🪶_Lightweight-1a1a1a?style=flat-square" alt="Lightweight" />
<img src="https://img.shields.io/badge/🔓_Open_Source-1a1a1a?style=flat-square" alt="Open Source" />
<img src="https://img.shields.io/badge/🚫_Zero_Telemetry-1a1a1a?style=flat-square" alt="Zero Telemetry" />
<img src="https://img.shields.io/badge/🎬_4K%2FHDR-1a1a1a?style=flat-square" alt="4K/HDR" />

<br><br>

[![Author](https://img.shields.io/badge/Author-Joel%20Biju%20%289ixe%29-1F2328?style=for-the-badge&logo=github&logoColor=white)](https://github.com/9ixe)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/9ixe/Odeon)
[![Engine](https://img.shields.io/badge/Engine-libmpv%20%7C%20D3D11-800020?style=for-the-badge&logoColor=white)](https://mpv.io)
[![License](https://img.shields.io/badge/License-GPL--3.0-6366F1?style=for-the-badge)](LICENSE)
<br>
[![GitHub Repo stars](https://img.shields.io/github/stars/9ixe/Odeon?style=for-the-badge&color=F5A623&logo=github&logoColor=white)](https://github.com/9ixe/Odeon/stargazers)
[![GitHub last commit](https://img.shields.io/github/last-commit/9ixe/Odeon?style=for-the-badge&logo=git&logoColor=white)](https://github.com/9ixe/Odeon/commits)
[![GitHub all releases](https://img.shields.io/github/downloads/9ixe/Odeon/total?style=for-the-badge&color=10B981&logo=windows&logoColor=white)](https://github.com/9ixe/Odeon/releases)

<br>

[![Download Odeon](https://img.shields.io/badge/⬇%20Download%20Odeon-E50914?style=for-the-badge&logoColor=white)](https://github.com/9ixe/Odeon/releases)

</div>

<br>

---

## What Is Odeon?

Odeon is a **lightweight, open-source media player for Windows**, built on [libmpv](https://mpv.io) (using [shinchiro's Windows builds](https://github.com/shinchiro/mpv-winbuild-cmake)) and [WinUI 3](https://learn.microsoft.com/windows/apps/winui/). It delivers **hardware-accelerated 4K/HDR playback** via Direct3D 11, **zero telemetry**, and a clean, distraction-free interface — all in a small, fast-loading package with no bundled bloatware.

| | |
| :--- | :--- |
| 🎞️ | Plays nearly every format without codec packs (MKV, MP4, AVI, WebM, FLAC, AAC, Opus) |
| ⚡ | **Hardware-accelerated 4K/HDR playback** via Direct3D 11 |
| 🔒 | **Zero telemetry** — no tracking, no analytics, no phone-home |
| 🎨 | **3 Built-in Accent Themes** — Netflix Style (crimson), Default Windows Dynamic Accent (system color), and Monochromatic (pure white) |
| 💬 | **Customizable subtitle rendering** with forced Futura PT typography |

### The Name

> *"In classical antiquity, an **Odeon** (from Ancient Greek ᾨδεῖον — literally 'a place for singing') was an intimate, roofed amphitheater engineered specifically for musical performances and acoustic mastery."*

Unlike colossal open-air arenas, ancient Odeons were enclosed sanctuaries designed for focused immersion and artistic purity. **Odeon** translates this philosophy into desktop media playback — an elegant, distraction-free auditorium where your cinema takes center stage.

---

## Why Odeon?

Most media players for Windows have grown into bloated media centers — background library scanners, intrusive telemetry, casting services, nested settings menus. Odeon strips all of that away.

<table width="100%">
<tr>
<td width="50%" valign="top">

**🪶 Lightweight**
<br>Small footprint, fast startup, no bundled bloatware.

</td>
<td width="50%" valign="top">

**🔓 Open Source**
<br>GPL-3.0 licensed, with fully auditable source code.

</td>
</tr>
<tr>
<td valign="top">

**🚫 No Telemetry**
<br>100% offline. No analytics SDKs, ever.

</td>
<td valign="top">

**⚙️ libmpv Playback**
<br>Powered by shinchiro's battle-tested libmpv runtime.

</td>
</tr>
<tr>
<td valign="top">

**🎞️ 4K/HDR Hardware Decoding**
<br>Smooth playback via Direct3D 11.

</td>
<td valign="top">

**🎨 3 Accent Themes**
<br>Netflix Style (Crimson), Default Windows Dynamic Accent, and Monochromatic (Pure White) — all over native Windows acrylic.

</td>
</tr>
<tr>
<td valign="top">

**🪟 Modern Windows Design**
<br>Built natively with WinUI 3 for Windows 10/11.

</td>
<td valign="top">

**💬 Subtitle Customization**
<br>Forced typography and a native ASS/SSA override pipeline.

</td>
</tr>
</table>

<div align="center">

**Odeon is for people who want to press play and enjoy their media — nothing more, nothing less — in a minimal and beautifully crafted experience.**

</div>

---

## Features

### 🎞️ 4K & HDR Hardware-Accelerated Playback

- **Hardware decoding** via Direct3D 11 (`--hwdec=auto-copy`) for smooth 4K/HDR content
- Powered by the high-performance **libmpv** engine ([shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake))
- Near-instantaneous playback startup — no cold-start delays
- Broad format support with no extra codec packs needed: MKV, MP4, AVI, WebM, MOV, FLAC, AAC, Opus, DTS, TrueHD, and more
- DXGI composition swap chain for pixel-accurate rendering at native resolution

### 🖤 Modern Windows Media Player UI & Themes

- WinUI 3 acrylic, glassmorphic backdrops on side panels and context menus
- Smart, centered titlebar that automatically strips file extensions
- Auto-dismissing HUD for volume, seek, and status notifications
- **Properties HUD** (`Tab`) — instant overlay with resolution, bitrate, duration, and file size
- Dynamic seekbar with spring-physics scrubbing
- **3 Curated Visual Accent Themes** (all sharing the same native Windows acrylic background):
  - **Netflix Style**: Bold crimson/red accent highlights across controls and active elements (Netflix-inspired)
  - **Default Windows Dynamic Accent**: Seamlessly synchronizes with your active Windows 10/11 system accent color
  - **Monochromatic**: Ultra-clean, distraction-free minimalist pure white accents

### 💬 Subtitle Customization

- **Forced Futura PT Medium & Linotte typography** across all subtitles and UI elements for a clean, cinematic look
- Native ASS/SSA override pipeline: strips hardcoded fonts/colors while preserving timing and positioning
- GDI font discovery via `AddFontResourceExW(FR_PRIVATE)` — no embedded font blobs, no cache management
- Redesigned subtitle side panel with instant track switching

### 🔊 Audio Controls

- Redesigned Audio panel for quick access during playback
- Manual **audio timing offset** adjustment to correct sync issues on mismatched files

### ▶️ Play Next

- Full **drag-and-drop reordering** of upcoming items
- Multi-select mode with bulk actions
- Dedicated Play Next side panel
- Mini player mode with compact playback controls

### 🔒 Privacy-Focused, Zero Telemetry

- All tracking SDKs (App Center, Sentry, etc.) fully purged from source
- Odeon never communicates with remote analytics servers
- Complete offline operation — no internet connection required

### 🌐 Local & Network Streaming

- Play files from local storage, external drives, and network shares (SMB, NAS, HTTP/HTTPS)
- No background library scanning — instant file access

<div align="right"><a href="#odeon">⬆ Back to top</a></div>

---

## Installation

Odeon is distributed as a standalone, self-contained Windows package:

1. Download the latest **Odeon build** (or `.zip` bundle) from [Releases](https://github.com/9ixe/Odeon/releases).
2. Double-click the `.msixbundle` → click **Install** in the Windows App Installer window.
3. Launch Odeon from your Start Menu and set it as your default player.

> [!NOTE]
> The `mpv-2.dll` runtime is included in the package — no separate installation required.

---

## Building from Source

### Prerequisites

* **Windows 10 (Build 1903+)** or **Windows 11**
* **Visual Studio 2022** with:
  * Universal Windows Platform development workload
  * Windows 10/11 SDK (10.0.2610.0 or compatible)
* **Windows Developer Mode** enabled
* **`mpv-2.dll`** — Download the `dev` package from [shinchiro's mpv-winbuild-cmake releases](https://github.com/shinchiro/mpv-winbuild-cmake/releases) and place `mpv-2.dll` in the `Odeon/` project folder (git-ignored).

### Build Steps

```bash
git clone https://github.com/9ixe/Odeon.git
# Open Odeon.sln in Visual Studio 2022
# Set Configuration: Release, Platform: x64
# Build or Deploy the Odeon project
```

---

## FAQ

<details>
<summary>🔄 <strong>Is Odeon a good VLC alternative for Windows?</strong></summary>
<br>

If you want a fast, no-telemetry media player that just plays a file without extra menus, Odeon is a strong lightweight VLC alternative. VLC still has a larger overall feature set (a built-in streaming server, a broader plugin ecosystem, and support for macOS/Linux/mobile), so if you rely on those specific features, VLC may still be the better choice. For everyday local playback on Windows, Odeon aims to feel lighter, faster, and more modern.

</details>

<details>
<summary>💸 <strong>Is Odeon free and open source?</strong></summary>
<br>

Yes. Odeon is completely free and licensed under GPL-3.0 — the full source code is available in this repository.

</details>

<details>
<summary>🪟 <strong>Does Odeon work on Windows 10 and Windows 11?</strong></summary>
<br>

Yes. Odeon supports Windows 10 (Build 1903 or later) and Windows 11, 64-bit, with a native WinUI 3 interface designed for both.

</details>

<details>
<summary>🎞️ <strong>Does Odeon support 4K and HDR playback?</strong></summary>
<br>

Yes. Odeon is a 4K video player for Windows with HDR support, using Direct3D 11 hardware decoding (`--hwdec=auto-copy`) for smooth, high-resolution playback.

</details>

<details>
<summary>⚡ <strong>Does Odeon use hardware acceleration?</strong></summary>
<br>

Yes. Decoding runs through Direct3D 11, and decoded frames are composited via a DXGI swap chain for pixel-accurate rendering at native resolution.

</details>

<details>
<summary>💬 <strong>Does Odeon support subtitles?</strong></summary>
<br>

Yes. Odeon includes a redesigned subtitle side panel with instant track switching, forced Futura PT Medium typography, and a native ASS/SSA override pipeline that preserves timing and positioning while normalizing fonts and colors.

</details>

<details>
<summary>🎨 <strong>What visual accent themes are available in Odeon?</strong></summary>
<br>

All themes share the **same native Windows acrylic background** — only the accent colors change to suit your style:
- **Netflix Style**: Bold crimson/red accent highlights across controls, badges, and active elements (Netflix-inspired).
- **Default Windows Dynamic Accent**: Automatically synchronizes with your active Windows 10/11 system accent color in real time.
- **Monochromatic**: Ultra-clean, distraction-free minimalist aesthetic featuring crisp, pure white accents.

*(Note: Odeon is an independent project and is not affiliated with, endorsed by, or connected to Netflix.)*

</details>

<details>
<summary>🔒 <strong>Does Odeon collect any telemetry?</strong></summary>
<br>

No. All tracking SDKs (App Center, Sentry, etc.) have been completely removed. Odeon is a privacy-focused, no-telemetry media player that runs 100% offline and never phones home.

</details>

<details>
<summary>⚙️ <strong>What playback engine does Odeon use?</strong></summary>
<br>

Odeon uses **libmpv** as its playback engine, powered by **shinchiro's** official Windows build toolchain ([shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake)), paired with a Direct3D 11 rendering pipeline — a massive upgrade from the legacy LibVLC engine.

</details>

<details>
<summary>🔀 <strong>What is the difference between Odeon and Screenbox?</strong></summary>
<br>

Odeon is a modern fork of [Screenbox](https://github.com/huynhsontung/Screenbox) that replaces LibVLC with libmpv (shinchiro builds) for the playback engine, removes all telemetry, adds 3 curated accent themes (Netflix Style, Default Windows Dynamic Accent, Monochromatic), implements forced subtitle typography and an ASS/SSA override pipeline, redesigns Play Next with drag-and-drop reordering, and strips unnecessary bloat.

</details>

<details>
<summary>🖥️ <strong>What are the system requirements?</strong></summary>
<br>

Windows 10 (Build 1903 or later) or Windows 11, 64-bit. Odeon has no separate installer dependencies — the mpv-2.dll runtime ships inside the package.

</details>

<details>
<summary>🔤 <strong>What is the Futura PT Medium font requirement?</strong></summary>
<br>

Odeon uses Futura PT Medium as its default subtitle and UI typeface for a consistent, cinematic look. You do not need to install the font yourself as it comes bundled with the app. If any issues occur, it will gracefully fall back to Segoe UI.

</details>

<details>
<summary>🌐 <strong>Can Odeon play network streams?</strong></summary>
<br>

Yes. Odeon supports SMB, NAS, HTTP/HTTPS network shares, and any stream URL supported by libmpv.

</details>

<div align="right"><a href="#odeon">⬆ Back to top</a></div>

---

## Contributing

Bug reports, feature requests, and pull requests are all welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines. If Odeon is useful to you, starring the repository is one of the easiest ways to help other people find it.

---

## Credits

| | |
| :--- | :--- |
| **Author & Maintainer** | [Joel Biju (9ixe)](https://github.com/9ixe) |
| **Original Project** | Fork of [Screenbox](https://github.com/huynhsontung/Screenbox) by [Huynh Son Tung (huyn)](https://github.com/huynhsontung) |
| **Playback Engine** | [libmpv](https://mpv.io) with Direct3D 11 composition |
| **Windows libmpv Builds** | Native `mpv-2.dll` toolchain & binaries by [shinchiro](https://github.com/shinchiro/mpv-winbuild-cmake) |
| **Architecture References** | [Richasy/mpv-winui](https://github.com/Richasy/mpv-winui), [WangyuHello/HotPotPlayer](https://github.com/WangyuHello/HotPotPlayer) |
| **License** | [GNU General Public License v3.0](LICENSE) |

---

<div align="center">

### [⬇ Download Odeon](https://github.com/9ixe/Odeon/releases) · [Report a Bug](https://github.com/9ixe/Odeon/issues) · [Contribute](CONTRIBUTING.md)

<sub>Made for people who just want to press play. 🎬</sub>

</div>
