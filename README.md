<div align="center">

  <img width="128" height="128" src="Odeon/Assets/StoreLogo.scale-400.png" alt="Odeon app icon — lightweight, open-source Windows media player powered by libmpv">

  # Odeon — Lightweight, Open-Source Media Player for Windows 10/11

  **A distraction-free, minimalist video and audio player for Windows, powered by libmpv.**
  <br>
  *The lightweight, no-telemetry alternative to VLC — fast startup, 4K/HDR hardware decoding, and a clean WinUI 3 interface.*

  <br>

  [![Author](https://img.shields.io/badge/Author-Joel%20Biju%20%289ixe%29-1F2328?style=for-the-badge&logo=github&logoColor=white)](https://github.com/9ixe)
  [![Telemetry](https://img.shields.io/badge/Telemetry-Zero%20%2F%20None-10B981?style=for-the-badge&logo=shield&logoColor=white)](PRIVACY.md)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/9ixe/Odeon)
  [![Engine](https://img.shields.io/badge/Engine-libmpv%20%7C%20D3D11-800020?style=for-the-badge&logoColor=white)](https://mpv.io)
  [![License](https://img.shields.io/badge/License-GPL--3.0-6366F1?style=for-the-badge)](LICENSE)
  <br>
  [![GitHub Repo stars](https://img.shields.io/github/stars/9ixe/Odeon?style=for-the-badge&color=F5A623&logo=github&logoColor=white)](https://github.com/9ixe/Odeon/stargazers)
  [![GitHub last commit](https://img.shields.io/github/last-commit/9ixe/Odeon?style=for-the-badge&logo=git&logoColor=white)](https://github.com/9ixe/Odeon/commits)
  [![GitHub all releases](https://img.shields.io/github/downloads/9ixe/Odeon/total?style=for-the-badge&color=10B981&logo=windows&logoColor=white)](https://github.com/9ixe/Odeon/releases)

</div>

---

## Table of Contents

- [Quick Start](#quick-start)
- [What Is Odeon?](#what-is-odeon)
- [Screenshots](#screenshots)
- [Why Odeon?](#why-odeon)
- [Features](#features)
- [Odeon vs Other Media Players](#odeon-vs-other-media-players)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Installation](#installation)
- [Building from Source](#building-from-source)
- [The Name](#the-name)
- [FAQ](#faq)
- [Contributing](#contributing)
- [Credits](#credits)

---

## Quick Start

1. Download the latest **`Odeon_2.0.0.0_x64.msixbundle`** from [Releases](https://github.com/9ixe/Odeon/releases).
2. Double-click the `.msixbundle` → click **Install**.
3. Launch Odeon from your Start Menu — done.

> [!NOTE]
> `mpv-2.dll` is included in the package. No separate installation required.

---

## What Is Odeon?

Odeon is a **lightweight, open-source media player for Windows** built on [libmpv](https://mpv.io) and [WinUI 3](https://learn.microsoft.com/windows/apps/winui/). It's an opinionated, debloated fork of [Screenbox](https://github.com/huyn-net/Screenbox) that replaces LibVLC with libmpv for superior playback performance.

Whether you're looking for a fast **video player for Windows 11**, a privacy-respecting **alternative to VLC**, or simply a clean player without background library scanning, casting menus, or ads — Odeon focuses on one job, playing your media well:

- Plays every format without codec packs (MKV, MP4, AVI, WebM, FLAC, AAC, Opus)
- Uses **hardware-accelerated 4K/HDR playback** via Direct3D 11
- Collects **zero telemetry** — no tracking, no analytics, no phone-home
- Has a **clean, modern WinUI interface** inspired by Netflix-style dark design
- Features **customizable subtitle rendering** with forced Futura PT typography

---

## Why Odeon?

Most media players have become bloated media centers — cluttered with background library scanners, intrusive telemetry, casting services, and nested menus. Odeon strips all of that away.

**Odeon is for people who want to press play and watch a video.** Nothing more.

---

## Features

### 4K & Hardware-Accelerated Playback

- **Hardware decoding** via Direct3D 11 (`--hwdec=auto-copy`) for smooth 4K/HDR content
- Near-instantaneous playback startup — no cold-start delays
- Supports virtually every container and codec: MKV, MP4, AVI, WebM, FLAC, AAC, Opus, DTS, TrueHD, and more
- DXGI composition swap chain for pixel-accurate rendering at native resolution

### Customizable Subtitle Rendering

- **Forced Futura PT Medium typography** across all subtitles — clean, cinematic, consistent
- Native ASS/SSA override pipeline: strips hardcoded fonts/colors, preserves timing and positioning
- GDI font discovery via `AddFontResourceExW(FR_PRIVATE)` — no embedded font blobs, no cache management
- Dedicated subtitle side panel with instant track switching

### Clean, Minimalist Interface

- **Netflix-style red accent** on a dark, glassmorphic WinUI canvas
- WinUI acrylic backdrops with blur effects on side panels and context menus
- Smart centered titlebar — auto-strips file extensions for a clean look
- Auto-dismissing HUD for volume, seek, and status notifications
- Glassmorphic **Properties HUD** (`Tab`) — instant overlay with video resolution, bitrate, duration, and file size
- Dynamic seekbar with spring physics during scrubbing

### Zero Telemetry, 100% Offline

- All tracking SDKs (App Center, Sentry, etc.) fully purged from source
- Odeon never communicates with remote analytics servers
- Complete offline operation — no internet connection required

### Play Queue & Multi-Track Support

- Full **drag-and-drop reorder** in the play queue
- Multi-select mode with bulk actions
- Dedicated side panels for audio tracks, subtitles, and queue management
- Mini player mode with compact playback controls

### Local & Network Streaming

- Play files from local storage, external drives, and network shares (SMB, NAS, HTTP/HTTPS)
- No background library scanning — instant file access

---

## Odeon vs Other Media Players

| Feature | **Odeon** | VLC | MPC-HC | mpv.net | Screenbox |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Engine** | libmpv | LibVLC | DirectShow | libmpv | LibVLC |
| **UI Framework** | WinUI 3 | Qt | Win32 | WinForms | WinUI 3 |
| **4K HW Decoding** | ✅ D3D11 | ✅ | ✅ | ✅ | ✅ |
| **Zero Telemetry** | ✅ | ❌ | ✅ | ✅ | ❌ |
| **Modern Dark UI** | ✅ | ❌ | ❌ | Partial | ✅ |
| **Glassmorphic Panels** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Custom Subtitle Fonts** | ✅ Forced | Manual | ❌ | Manual | ❌ |
| **Play Queue** | ✅ DnD | ✅ | ❌ | ❌ | ✅ |
| **Open Source** | ✅ GPL-3.0 | ✅ GPL-2.0 | ✅ GPL-3.0 | ✅ GPL-3.0 | ✅ MIT |
| **Windows 11 Design** | ✅ | ❌ | ❌ | ❌ | ✅ |

---

## Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| **`Tab`** | Toggle Glassmorphic Video Properties HUD |
| **`Space`** / **`K`** | Play / Pause |
| **`F`** / **`F11`** | Toggle Fullscreen |
| **`←`** / **`→`** | Seek 5 seconds backward / forward |
| **`J`** / **`L`** | Seek 10 seconds backward / forward |
| **`↑`** / **`↓`** | Volume Up / Down |
| **`M`** | Toggle Mute |
| **`1` – `4`** | Window Resize Presets (50%, 100%, 150%, 200%) |
| **`Esc`** | Exit Fullscreen / Dismiss Active Menus |
| **`C`** | Toggle Subtitle Side Panel |
| **`I`** | Restore from Mini Player |

---

## Installation

Odeon is distributed as a standalone, self-contained Windows package:

1. Download the latest **`Odeon_2.0.0.0_x64.msixbundle`** (or `.zip` bundle) from [Releases](https://github.com/9ixe/Odeon/releases).
2. Double-click the `.msixbundle` → click **Install** in the Windows App Installer window.
3. Launch Odeon from your Start Menu and set it as your default player.

> [!NOTE]
> The `mpv-2.dll` runtime is included in the package — no separate installation required.

> [!TIP]
> For the intended typography, install the **Futura PT Medium** font on your system (download, right-click → *Install for all users*).

---

## Building from Source

### Prerequisites

* **Windows 10 (Build 1903+)** or **Windows 11**
* **Futura PT Medium** font installed
* **Visual Studio 2022** with:
  * Universal Windows Platform development workload
  * Windows 10/11 SDK (10.0.2610.0 or compatible)
* **Windows Developer Mode** enabled
* **`mpv-2.dll`** — Download the `dev` package from [mpv shinchiro builds](https://github.com/shinchiro/mpv-winbuild-cmake/releases) and place it in the `Odeon/` project folder (git-ignored).

### Build Steps

```bash
git clone https://github.com/9ixe/Odeon.git
# Open Odeon.sln in Visual Studio 2022
# Set Configuration: Release, Platform: x64
# Build or Deploy the Odeon project
```

---

## The Name

> *"In classical antiquity, an **Odeon** (from Ancient Greek ᾨδεῖον — literally 'a place for singing') was an intimate, roofed amphitheater engineered specifically for musical performances and acoustic mastery."*

Unlike colossal open-air arenas, ancient Odeons were enclosed sanctuaries designed for focused immersion and artistic purity. **Odeon** translates this philosophy into desktop media playback — an elegant, distraction-free auditorium where your cinema takes center stage.

---

## FAQ

<details>
<summary><strong>What formats does Odeon support?</strong></summary>

Odeon plays MKV, MP4, AVI, WebM, MOV, FLAC, AAC, Opus, DTS, TrueHD, and virtually every other media format supported by libmpv. No codec packs needed.
</details>

<details>
<summary><strong>Does Odeon collect any telemetry?</strong></summary>

No. All tracking SDKs (App Center, Sentry, etc.) have been completely removed. Odeon is 100% offline and never phones home.
</details>

<details>
<summary><strong>Is Odeon free and open source?</strong></summary>

Yes. Odeon is completely free and licensed under GPL-3.0 — the full source code is available in this repository.
</details>

<details>
<summary><strong>Is Odeon a good alternative to VLC on Windows?</strong></summary>

If you want a fast, no-telemetry player that just plays a file without extra menus, Odeon is a strong fit. VLC still has a larger overall feature set (a built-in streaming server, a broader plugin ecosystem, and support for macOS/Linux/mobile), so if you rely on those specific features, VLC may still be the better choice. For everyday local playback on Windows, Odeon aims to feel lighter and faster.
</details>

<details>
<summary><strong>What are the system requirements?</strong></summary>

Windows 10 (Build 1903 or later) or Windows 11, 64-bit. Odeon has no separate installer dependencies — the mpv-2.dll runtime ships inside the package.
</details>

<details>
<summary><strong>What is the Futura PT Medium font requirement?</strong></summary>

Odeon uses Futura PT Medium as its default subtitle and UI typeface for a consistent, cinematic look. Download the font and install it for all users on Windows. Without it, subtitles fall back to system fonts.
</details>

<details>
<summary><strong>How does Odeon differ from Screenbox?</strong></summary>

Odeon is a fork of Screenbox that replaces LibVLC with libmpv for the playback engine, removes all telemetry, adds forced subtitle typography, redesigns the play queue with drag-and-drop, and strips unnecessary features like casting and PiP.
</details>

<details>
<summary><strong>Does Odeon support hardware acceleration?</strong></summary>

Yes. Odeon uses Direct3D 11 hardware decoding (`--hwdec=auto-copy`) for smooth 4K and HDR playback. Decoded frames are composited via a DXGI swap chain.
</details>

<details>
<summary><strong>Can Odeon play network streams?</strong></summary>

Yes. Odeon supports SMB, NAS, HTTP/HTTPS network shares, and any stream URL supported by libmpv.
</details>

---

## Contributing

Bug reports, feature requests, and pull requests are all welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines. If Odeon is useful to you, starring the repository is one of the easiest ways to help other people find it.

---

## Credits

- **Author & Maintainer:** [Joel Biju (9ixe)](https://github.com/9ixe)
- **Original Project:** Fork of [Screenbox](https://github.com/huyn-net/Screenbox) by [Huyn](https://github.com/huyn-net)
- **Playback Engine:** [libmpv](https://mpv.io) with Direct3D 11 composition
- **Architecture References:** [Richasy/mpv-winui](https://github.com/Richasy/mpv-winui), [WangyuHello/HotPotPlayer](https://github.com/WangyuHello/HotPotPlayer)
- **License:** [GNU General Public License v3.0](LICENSE)

---

<div align="center">

  **[Download Odeon](https://github.com/9ixe/Odeon/releases)** · [Report a Bug](https://github.com/9ixe/Odeon/issues) · [Contributing](CONTRIBUTING.md)

</div>
