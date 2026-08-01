# 🐱 NyaLauncher

> A modern, cross-platform lightweight Minecraft launcher built for elegance and freedom.
<br>
![License](https://img.shields.io/badge/license-GPLv3-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10-purple.svg)
![Avalonia](https://img.shields.io/badge/Avalonia-12.0-green.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)

---

## ✨ Overview

**NyaLauncher** is a cross-platform Minecraft launcher built with **Avalonia UI 12.0** and **.NET 10**.  
It is lightweight and fast, with a strong emphasis on **privacy** and **plugin extensibility**, giving you full control while enjoying the game.

---

## 🎯 Key Features

- 🚀 **Cross-platform** — Windows, macOS, and Linux with a nearly consistent look and feel.  
- 🔌 **Powerful plugin system** — Supports dynamic loading of third-party plugins to extend functionality.  
- 🛡️ **Privacy-first** — No telemetry or third-party tracking. Uses a custom token encryption approach to reduce token leakage via registry and other vectors.  
- ⚡ **Lightweight & efficient** — Built with .NET 10 native AOT compilation; no unnecessary features, customizable to your needs.  
- 🎨 **Modern UI** — Smooth design based on Avalonia, with customization support.  
- ✊ **Completely original** — This project does not borrow code from other launchers and does not intentionally mimic them.

---

## Implemented vs Planned Features
<table>
  <tr>
	<th>Feature</th>
	<th>Status</th>
  </tr>
  <tr>
	<td>🚀 Cross-platform support</td>
	<td><span>✅ Implemented</span></td>
  </tr>
  <tr>
	<td>⚡️ Smooth animations</td>
	<td><span>✅ Implemented</span></td>
  </tr>
  <tr>
	<td>🎮 Offline account launch</td>
	<td><span>✅ Implemented</span></td>
  </tr>
  <tr>
	<td>🔌 Plugin system</td>
	<td><span>🚧 In development</span></td>
  </tr>
  <tr>
	<td>🛡️ Privacy protections</td>
	<td><span>🚧 In development</span></td>
  </tr>
  <tr>
	<td>🎭 Custom themes</td>
	<td><span>🚧 In development</span></td>
  </tr>
  <tr>
	<td>🧩 Mod management</td>
	<td><span>🚧 In development</span></td>
  </tr>
  <tr>
	<td>🎮 Multiplayer......?</td>
	<td><span>🤔 Possibly planned; may be optional</span></td>
  </tr>
</table>

---

## 🎮 Offline Launch

- Supports scanning Minecraft home directories and per-version instance directories under `versions/<version>`.  
- Provides offline username validation and deterministic UUID generation without reading or relying on online account tokens.  
- Supports version inheritance, modern and legacy launch arguments, OS rules, and classpath construction.  
- Safely extracts natives and cleans temporary files after the game exits.  
- Selects the closest Java runtime that meets the version JSON minimum Java requirement from Minecraft runtime, `NYALAUNCHER_JAVA`, `JAVA_HOME`, or `PATH`; explicit configuration may allow using a higher major version.  
- Classpath merging distinguishes normal libraries, `natives-*`, `unsafe`, and other Maven classifiers to avoid overwriting identical LWJGL dependencies.  
- The launch page provides directory scanning, version selection, offline username, run status, and exit code information.

> Offline accounts cannot join servers that require official authentication; ensure the target version, libraries, and resources are fully installed before launching.

---

## 📦 Tech Stack

| Component       | Technology                     |
|----------------|-------------------------------|
| UI Framework    | Avalonia UI 12.0              |
| Runtime         | .NET 10                       |

---

## 🔧 Project Structure

| Project                            | Responsibilities                                 |
|-----------------------------------|--------------------------------------------------|
| NyaLauncher.Core                  | Core launch functionality for NyaLauncher        |
| NyaLauncher.Avalonia              | Frontend UI based on Avalonia                    |
| NyaLauncher.Avalonia.Animations   | Animation library for NyaLauncher UI             |
| NyaLauncher.MinecraftTokenCrypto  | (**Not public for certain reasons**) Encryption algorithm for Minecraft account tokens |

---

## 🛠️ Quick Start

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or newer  
- Windows 10+, macOS Ventura+, or Linux Kernel 5.0+  
- Desktop runtime for your platform

### Clone & Build

```
git clone https://github.com/your-username/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```

> Note: This project is still a work in progress. Any runtime or build issues may occur and are within expected limitations.

---

## Changelog

Recent changes

### v0.1.0-gp2

> `v0.1.0-gp2` is only the codename for the second v0.1.0 new-GUI preview; it is not written into the Core version.

- 与main同步更新。
- Rebuilt the frontend around an extensible docking workspace whose feature areas can dock horizontally or vertically.
- Added seam-based resizing and automatic edge sidebars when an area meets both the edge and size requirements.
- Added animated sidebar reveal, edge-to-edge dragging and swapping, plus continuous drag-to-restore behavior.
- Added full personalization for area names, descriptions, icons and actions, including creating and deleting custom areas.
- Persisted area layout, sizes, sidebars and personalization in a user-selectable directory with cross-platform defaults.
- Feature actions now navigate inside the current window and reuse the launch, download and settings GUI; personalization lives in Settings and `Esc` always opens Settings.
- Reduced the workspace header and footer, added resizing from every window edge and corner, and made navigated pages cover the workspace chrome.
- Removed redundant frontend build outputs and native debug symbols while preserving per-platform Windows, macOS and Linux releases.
- Refreshed the first-run default from the latest personalized layout, sidebar and component positions.
- Added an independent Component Library for drag-to-add, cross-area moves, and drag-back removal with immediate persistence.
- Feature areas now behave as independent desktops: components can be freely positioned without crossing boundaries, reflow proportionally on resize, and share a configurable global size.
- Added a cursor-following placement ghost while dragging components, including drops into revealed sidebars.
- Fixed sidebar conversion with populated areas and completed horizontal and vertical resize-threshold handling.
- Completed overlapping-component stacking: later placements stay above earlier ones, while the hovered component is highlighted temporarily on top and returns to its saved order on leave.
- Standardized the size, centering and state feedback of the minimize, maximize/restore and close vector icons.

v0.1.0pre1
- Split GUI into a separate library (NyaLauncher.Avalonia.Animations).  
- Improved several visual stutter issues.  
![v0.1.0pre1 main screen](img/v0.1.0pre1.png)
