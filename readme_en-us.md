# 🐱 NyaLauncher

> A modern, cross-platform lightweight Minecraft launcher, born for freedom.
<br>
![.NET](img/badges/dotnet.svg)
![Avalonia](img/badges/avalonia.svg)
![Platform](img/badges/platform.svg)
![License](img/badges/license.svg)

---

## ✨ Overview

**NyaLauncher** is a cross-platform Minecraft launcher built with **Avalonia UI 12.1.1** and **.NET 10**.<br>
It is not only lightweight and fast but also emphasizes **privacy protection** and **interface customization**, giving you full autonomous control while you enjoy the game.<br>
NyaLauncher is free software. Apart from the library files retained when necessary, all code is licensed under the [Apache License 2.0](LICENSE).<br>
The launcher performs no telemetry without your knowledge, never infringes on your privacy, and places no functional limitations on you.

---

## 📦 Tech Stack

| Component                    | Technology                       |
|------------------------------|----------------------------------|
| UI Framework                 | Avalonia UI 12.1.1               |
| Runtime                      | .NET 10                          |
| Component Extension Contract | .NET 10, no Avalonia dependency  |

---

## 🔧 Project Structure

| Project                                | Responsibilities                                                            |
|----------------------------------------|-----------------------------------------------------------------------------|
| NyaLauncher.Core                       | 🐱 Core launch functionality set of NyaLauncher                             |
| NyaLauncher.Avalonia                   | Frontend UI of NyaLauncher, built with Avalonia technology                  |
| NyaLauncher.Avalonia.Animations        | Animation library for the NyaLauncher frontend UI                           |
| NyaLauncher.Plugin.Abstractions        | UI-framework-independent component contracts, geometry, elements, runtime state, and validation |
| NyaLauncher.MinecraftTokenCrypto       | (**Closed-source because the algorithm is not suitable for public disclosure**) Encryption algorithm/storage for Minecraft premium account login tokens |

---

## 🔃 Update Plan

### 📝 Version Naming Rules
| Stage         | Meaning                                                                      |
|---------------|------------------------------------------------------------------------------|
| beta          | Writing stage of the launcher, completely unusable                           |
| preview       | Testing stage, partially usable but not recommended for daily use (the current stage of 0.1.0preview-3) |
| release       | Official release, fully usable                                               |
| gp (special)  | Special version number used on the newgui branch, corresponding to the main branch's preview |

### Planned Features
- Plugin functionality (successfully verified on the downstream testplug branch)
- Custom themes (expected to be released in the next preview version)
- Multi-language support (schedule to be determined)
- AI-assisted translation/error checking (unknown)
- Online multiplayer (???)

---

## 🛠️ Quick Start

### 🪟🍎🐧 Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or newer
- Windows 10+, macOS Ventura+, Linux Kernel 5.0+
- Desktop runtime (Windows/macOS/Linux)
> HarmonyOS porting is pending.

### 🔧 Clone & Build

```bash
git clone https://github.com/redstore-noob/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```

---

## 📈 Changelog
Recent changes

v0.1.0-preview3
- Completed the newgui functionality and merged it back into the main branch
- Added a large number of components
- Conducted a small-scale refactoring of the Core module (progress: 25/100%)
- Added Minecraft-related download features
- Added Log functionality to save runtime files produced by the launcher/game
- Fixed hard-coded styling issues in the frontend and made minor optimizations to other problems
- The plugin system is under testing, and the first usable API is coming soon
- Redundant/dead code in the backend has been removed
- Removed some code from the animations module, refactoring is coming soon
- Changed the naming rules of release versions
- Removed Herobrine
![v0.1.0-preview3 main window screenshot](img/v0.1.0preview-3-mainwindow.png)
![v0.1.0-preview3 game management screenshot](img/v0.1.0preview-3-game.png)
![v0.1.0-preview3 account management screenshot](img/v0.1.0preview-3-account.png)

v0.1.0-gp2 (newgui branch)

> `v0.1.0-gp2` only marks the second UI iteration of v0.1.0 newgui and is not written into the Core version.<br>This version is not related to the main branch.

- Rebuilt the GUI (on the newgui branch); the home page became customizable component blocks, increasing customization freedom (not yet complete, and the old GUI is preserved on the main branch)
- Added offline launch and premium/online launch
- Fixed a bug in readme.md (?)
- Added multi-account management
- Added configuration saving; after configuring once, it finally persists 😭
- Optimized Java detection, fixing the previous issue where Java could start but could not be used
- Removed Herobrine
![v0.1.0-gp2 main window screenshot](img/v0.1.0pre2-mainwindow.png)
![v0.1.0-gp2 launch screen screenshot](img/v0.1.0pre2.png)
![v0.1.0-gp2 settings screenshot](img/v0.1.0pre2-settings.png)
![v0.1.0-gp2 personalization screenshot](img/v0.1.0pre2-settings2.png)

v0.1.0-pre1
- Split the GUI out of the user interface into a separate library (NyaLauncher.Avalonia.Animations)
- Improved some of the visual stuttering issues
- Removed Herobrine
![v0.1.0-pre1 main window screenshot](img/v0.1.0pre1.png)