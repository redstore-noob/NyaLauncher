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
- Fixed classpath dependency overwrite issue during version inheritance.  
- Java version requirement changed to a minimum constraint; higher major versions are supported. Verified Minecraft 1.21.4 can run on Java 21 and Java 25.

v0.1.0pre1
- Split GUI into a separate library (NyaLauncher.Avalonia.Animations).  
- Improved several visual stutter issues.  
![v0.1.0pre1 main screen](img/v0.1.0pre1.png)
