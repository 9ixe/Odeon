<div align="center">

  <img width="128" height="128" src="Odeon/Assets/StoreLogo.scale-400.png" alt="Odeon Logo">

  # Odeon

  **An exquisite, distraction-free desktop media player for Windows.**
  <br>
  *Engineered for acoustic purity, curated typography, and uncompromising simplicity.*

  <br>

  [![Author](https://img.shields.io/badge/Author-Joel%20Biju%20%289ixe%29-1F2328?style=for-the-badge&logo=github&logoColor=white)](https://github.com/9ixe)
  [![Telemetry](https://img.shields.io/badge/Telemetry-Zero%20%2F%20None-10B981?style=for-the-badge&logo=shield&logoColor=white)](PRIVACY.md)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/9ixe/Odeon)
  [![Engine](https://img.shields.io/badge/Engine-LibVLCSharp-FF8800?style=for-the-badge&logo=vlcmediaplayer&logoColor=white)](https://github.com/videolan/libvlcsharp)
  [![License](https://img.shields.io/badge/License-GPL--3.0-6366F1?style=for-the-badge)](LICENSE)

</div>

---

## 🏛️ The Meaning & Philosophy Behind the Name

> *"In classical antiquity, an **Odeon** (from Ancient Greek ᾨδεῖον, Ōideion — literally 'a place for singing') was an intimate, roofed amphitheater engineered specifically for musical performances, acoustic mastery, and poetry competitions."*

Unlike colossal open-air arenas built for mass spectacles, ancient **Odeons** were enclosed sanctuaries designed with deliberate acoustic precision—spaces crafted for focused immersion, artistic purity, and intimate resonance.

**Odeon** translates this classical architectural philosophy into desktop media playback. Developed by [**Joel Biju (9ixe)**](https://github.com/9ixe) as an opinionated, debloated fork of [Screenbox](https://github.com/huyn-net/Screenbox), Odeon strips away the bloat, background telemetry, and visual noise common in contemporary media suites. The goal is simple: to provide an elegant, distraction-free auditorium where your cinema and music take center stage.

---

## 💡 Why Odeon? Understanding the Vision

Most modern media players have slowly morphed into bulky media centers—cluttered with background library scanners, intrusive telemetry trackers, casting services, and nested menus that distract from the pure act of watching a video.

Odeon was built from the ground up to reverse this trend. It is designed to be **instant, lightweight, and visually refined**, pairing a modern Windows aesthetic with the industrial-strength decoding engine of VLC.

---

## 🎯 What Sets Odeon Apart from Screenbox

### 🔤 Curated Typography & Intelligent Subtitle Engine
In conventional players, subtitles are frequently treated as an afterthought—rendered either in generic system typefaces or forced to display garish, inconsistent hardcoded styles embedded in ASS/SSA subtitle scripts.

Odeon introduces a bespoke typographic and subtitle architecture:
- **Unified Typographic Identity:** The entire interface, dialogs, and subtitle engine are built around **Futura PT Medium**—a timeless geometric sans-serif typeface selected for its balanced proportions, high legibility at all distances, and cinematic feel.
- **Instantaneous In-Memory Subtitle Extraction:** Standard players often introduce noticeable lag or micro-stutters when parsing subtitle streams from heavy container files. Odeon integrates an ultra-fast, in-memory Matroska (MKV) parser that extracts and stages subtitle tracks asynchronously with zero disk I/O bottlenecks, ensuring subtitles appear instantly upon video launch or track switching.
- **Intelligent Subtitle Style Override:** Odeon includes a specialized subtitle parsing engine accessible directly from the player controls. When enabled, it dynamically normalizes complex embedded ASS/SSA subtitle tracks, stripping away chaotic custom colors and fonts while preserving timing, rendering dialogues in crisp, perfectly outlined *Futura PT Medium*.
- **Isolated Cache Storage:** Overridden subtitle tracks are stored cleanly in the app's local cache directory without touching your original video files, and can be safely cleared at any time.

> [!NOTE]
> **System Font Requirement:** To experience Odeon's intended typography across the player UI and subtitle renderer, **you must have the `Futura PT Medium` font installed on your Windows system** (simply download the font and select *Install for all users*).

### ✂️ Debloated Core & Distraction-Free Philosophy
- **Zero Telemetry & 100% Offline:** All tracking and diagnostic SDKs (such as Microsoft App Center and Sentry) have been completely purged from the source code. Odeon never communicates with remote analytics servers or monitors your viewing habits.
- **Stripped-Down Interface:** Removed cluttered casting overlays, PiP toggles, play queue drawers, and unnecessary controls. The interface only presents what you need during playback.
- **Clean Context Menu:** The right-click menu has been redesigned to offer fast, direct access to critical playback actions without multi-tier submenus.
- **Instant Playback Focus:** Launch video and audio files with near-zero latency, free from background indexing routines or resource-heavy library watchers.

### ✨ Handcrafted Visual & Interactive Refinements
- **Curated Monochrome Palette:** Replaced unpredictable OS system accent colors with a clean, high-contrast white and dark aesthetic that blends into the background during playback.
- **Smart Centered Titlebar:** Automatically parses video filenames, stripping file extensions (`.mkv`, `.mp4`, etc.) and brackets to display a clean, centered title across both windowed and fullscreen modes.
- **Glassmorphic Properties HUD (`Tab`):** Pressing `Tab` at any moment reveals an instant frosted-glass overlay displaying the video's Title, Resolution, Bitrate, Duration, and File Size without interrupting playback.
- **Dynamic Interactive Seekbar:** The progress bar features subtle spring physics that scale and expand dynamically during scrubbing, complete with smooth rounded track geometry.
- **Dedicated Track Switchers:** Separate, dedicated controls for Audio Tracks and Subtitles allow for rapid, 1-click switching between language streams.
- **Auto-Dismissing HUD:** Clean on-screen notifications for volume and seek adjustments that automatically dismiss after 1 second.

### ⚡ Robust Playback Architecture
Underneath its minimalist exterior, Odeon is powered by **LibVLCSharp** and the native LibVLC core:
- **Near-Instantaneous Playback Startup:** Optimized pipeline eliminates cold-start bottlenecks, redundant decoder resets, and background I/O delays, launching high-bitrate media files with near-zero latency.
- **Comprehensive Codec Support:** Effortlessly plays virtually every container, video format, and audio standard (MKV, MP4, AVI, WebM, FLAC, AAC, Opus, etc.).
- **Hardware Acceleration:** Utilizes DirectX / Direct3D hardware decoding for smooth 4K/HDR playback with minimal CPU usage.
- **Local & Network Streaming:** Full support for playing media from local storage, external drives, and network shares (SMB, NAS, HTTP/HTTPS).

---

## ⌨️ Keyboard Shortcuts

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

---

## 📥 Installation

Odeon is distributed as a standalone, self-contained Windows package:

1. Download the latest **`Odeon_1.0.0.0_x64.msixbundle`** (or `.zip` bundle) from the **[Releases](https://github.com/9ixe/Odeon/releases)** tab.
2. **Double-click the `.msixbundle` file** and click **Install** in the Windows App Installer window.
3. Launch **Odeon** from your Start Menu and set it as your default player!

*(Alternative: If extracting from a full `.zip` bundle, you can also right-click `Install.ps1` and choose **Run with PowerShell**).*

---

## 🛠️ Building from Source

### Prerequisites
* **Windows 10 (Build 1903+)** or **Windows 11**
* **Futura PT Medium** font installed in Windows
* **Visual Studio 2022** with the following workloads:
  * Universal Windows Platform development
  * Windows 10/11 SDK (10.0.26100.0 or compatible)
* **Windows Developer Mode** enabled in Settings

### Build Steps
```bash
# 1. Clone the repository
git clone https://github.com/9ixe/Odeon.git

# 2. Open Odeon.sln in Visual Studio 2022
# 3. Set Configuration to Release and Platform to x64
# 4. Build or Deploy the Odeon project (or Publish -> Create App Packages)
```

---

## 📜 Credits & Acknowledgments

- **Author & Maintainer:** [**Joel Biju (9ixe)**](https://github.com/9ixe) — Forked, customized, and debloated.
- **Original Project:** Odeon is an independent fork of [Screenbox](https://github.com/huyn-net/Screenbox), originally developed by [Huyn](https://github.com/huyn-net). Special thanks to Huyn and all original contributors.
- **Playback Engine:** Powered by [LibVLCSharp / LibVLC](https://github.com/videolan/libvlcsharp) from the VideoLAN team.
- **License:** Licensed under the [GNU General Public License v3.0](LICENSE).
