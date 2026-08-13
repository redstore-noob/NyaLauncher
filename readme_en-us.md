# 🐱 NyaLauncher

> A modern, cross-platform lightweight Minecraft launcher built for elegance and freedom.
<br>
![.NET](img/badges/dotnet.svg)
![Avalonia](img/badges/avalonia.svg)
![Platform](img/badges/platform.svg)

---

## ✨ Overview

**NyaLauncher** is a cross-platform Minecraft launcher built with **Avalonia UI 12.1.1** and **.NET 10**.<br>
It is lightweight and fast, with a strong emphasis on **privacy** and **plugin extensibility**, giving you full control while enjoying the game.

---

## 🎯 Key Features

- 🚀 **Cross-platform** — Windows, macOS, and Linux with a nearly consistent look and feel.
- 🔌 **Extensible plugin framework** — Built-in and third-party components share one contract, with package discovery, consent, lifecycle, settings, and Minecraft instance extensions integrated.
- 🛡️ **Privacy-first** — No telemetry or third-party tracking. Uses a custom token encryption approach to reduce token leakage via registry and other vectors.
- ⚡ **Lightweight & efficient** — Built with .NET 10 and Avalonia, organized around focused responsibilities and on-demand extensions.
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
	<td>⬡ Polygon component framework</td>
	<td><span>✅ Foundation implemented</span></td>
  </tr>
  <tr>
	<td>🎮 Offline account launch</td>
	<td><span>✅ Implemented</span></td>
  </tr>
  <tr>
	<td>📥 Game version downloads</td>
	<td><span>✅ Basic installation flow implemented</span></td>
  </tr>
  <tr>
	<td>🔌 Plugin system</td>
	<td><span>✅ v1 foundation implemented</span></td>
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

## ⬡ Polygon Component Framework

- `NyaLauncher.Plugin.Abstractions` provides a public contract with no Avalonia dependency, allowing third-party extensions to describe components without referencing the launcher's UI implementation.
- Convex and concave polygon outlines can be declared with normalized `[0,1]` coordinates, together with separate preferred, minimum, and maximum sizes.
- Components can combine text, crop-aware images, buttons, progress bars, and dropdown menus; buttons and menu rows invoke asynchronous commands through stable action IDs.
- Each interactive component receives an independent runtime instance from its factory and updates text, image sources, progress, menu rows, enabled, visible, and indeterminate states through immutable snapshots with increasing `Revision` values.
- Definitions are validated before registration for IDs, geometry, sizes, elements, action references, and progress ranges; extensions can diagnose problems through stable error codes.
- The host clips and hit-tests against the real polygon outline, so transparent corners do not capture hover or clicks.
- Polygon components and legacy button components enter the same component catalog and reuse workspace drag-and-drop, relative positioning, scaling, stacking, and sidebar behavior.
- gp3 includes Account Selector, Game Instance Selector, Version Selection & Management, Launch Game, Skin & Cape, and Download Task Progress components. The existing instance component retains its quick dropdown, while the new version component opens the full management page.
- `workspace.json` stores only the stable component ID, feature area, relative coordinates, and stacking order; the provider restores component geometry, content definitions, and transient state.

> `v0.1.0-gp3` integrates package discovery, capability consent, lifecycle/unloading, declarative settings, components, transactional instance actions, and per-launch contributions.
> Authors may define a completely new mod protocol and Java loader without depending on Forge, Fabric, or another existing loader. See the
> [plugin development specification](NyaLauncher.Plugin.Abstractions/README.md) for package structure, APIs, examples, and safety boundaries. Plugins run in the launcher process; capability consent is not an operating-system sandbox.

---

## ⚙ Memory Settings

- Settings now provides a global maximum-memory slider from 512 MiB up to the machine's total physical memory, persisted to `config.json` in 256 MiB steps.
- Enabling automatic adjustment locks the global slider. Immediately before each launch, the launcher samples currently available physical memory, reserves at least 2 GiB for the system, caps the game at 75% of total memory, and derives that launch's global limit. Disabling it restores the slider and the saved manual limit.
- Per-instance minimum and maximum memory are sliders controlled by an Independent Adjustment switch. The switch is off by default, locking both sliders and making the instance use the global result unchanged. When enabled, effective `-Xmx` is the lower of the instance and global manual/automatic limits. If the instance minimum exceeds that result, `-Xms` is reduced with it to keep JVM arguments valid.

---

## ▦ Version Management

- Added a separate Version Selection & Management polygon component. A short press opens the management page and a long press anywhere visible still drags it; the existing instance dropdown is unchanged.
- The page can add and switch among Minecraft roots or `versions/<version>` folders, then select installed instances inside the chosen folder. Selection stays synchronized with the Launch page and quick dropdown.
- Version details separately report the instance ID, base Minecraft version, mod-loader name and exact version, version type, release time, Java requirement, and main class, and scan the effective game directory for mods, resource packs, shaders, and saved worlds.
- The instance selector, its quick-selection menu, the version list, and the detail heading display instance icons. Vanilla, Fabric, Forge, NeoForge, and other loaders use matching fallbacks, while PCL, MultiMC/Prism, CurseForge, Modrinth App, and ATLauncher packs prefer custom local or HTTPS artwork supplied by the pack author or publishing-site metadata. All icons preserve their full aspect ratio, so wide or tall artwork is not cropped, and the current menu item uses a separate selection badge.
- Mods, resource packs, shaders, and worlds use detail cards. Mod metadata supports Fabric, Quilt, Forge, NeoForge, and legacy Forge formats and reads names, authors, versions, descriptions, and embedded icons. Packs read `pack.mcmeta` and embedded images; worlds read `level.dat`, `icon.png`, the Minecraft version, creation date, and last-played time. Missing non-standard author or version fields are explicitly shown as unavailable.
- Vanilla instances prompt the user to install a mod loader. Loader-enabled instances with no mods correctly show zero mods, and the Shaders tab appears only when a `shaderpacks` folder exists.
- Enabling Independent Adjustment unlocks per-instance minimum and maximum sliders; by default they are locked and the global result is used, while the page previews the effective memory maximum.
- Global Java, window size, and extra JVM/game arguments are collapsed under Advanced Options in Settings by default. Instance Advanced Options are also collapsed and Follow Global is enabled by default; following displays and locks the global values, while disabling it restores and unlocks that instance's own advanced launch arguments.
- Version isolation can be explicitly enabled or disabled per instance. Without an explicit setting, the launcher checks PCL's `VersionArgumentIndieV2`/`VersionArgumentIndie`, recognizes HMCL, MultiMC/Prism, CurseForge, Modrinth App, and ATLauncher markers plus nested `minecraft`/`.minecraft` folders, then falls back to common content signals such as `mods`, `resourcepacks`, `shaderpacks`, `saves`, `config`, and `options.txt`. Instance lists and details show the detected provider and evidence.
- The launch working directory, mods, resource packs, shaders, configuration, saves, and folder shortcuts all consume the same layout result. Third-party instances with nested `minecraft`/`.minecraft` content no longer read the outer version folder, and wrapper folders containing standard `versions` metadata can be added directly.
- External instance folders containing `instance.cfg`, `minecraftinstance.json`, `profile.json`, or `instance.json` can be added directly for content browsing. Their native patch metadata is not yet converted into NyaLauncher's launch format, so physical renaming and isolation controls are disabled and launch attempts receive an explicit message instead of using incorrect arguments.
- Changing the version name physically renames the version folder and matching JSON/JAR, updates the JSON `id`, and repairs other version descriptors that reference the old ID through `inheritsFrom` or `jar`. Invalid names and existing targets are rejected.
- Buttons open the Minecraft root, selected version folder, mods folder, and saves folder; missing mods or saves folders are created before opening.
- Asynchronous details are accepted only while they still match the current folder and version, and stale details are cleared immediately during a switch or refresh. Version, mods, and saves shortcuts resolve the active layout again on click; Windows delegates opening to Explorer to avoid stale paths or incorrect directory shell associations.

---

## 📥 Game Downloads

- The Download page can select a release from Mojang's version manifest and install its version metadata, client, libraries, asset index, and asset objects into the current Minecraft folder.
- Installation reports seven stages: fetching metadata, analyzing the plan, downloading the client, downloading libraries, downloading the asset index, downloading assets, and final verification and installation.
- The progress window reports the current stage and detail, overall progress, download speed, transferred bytes, and file counts, and can cancel the active task.
- Files are SHA-1 verified. Existing valid files are reused, new files are written to temporary files before atomic replacement, and up to eight files download in parallel.
- Starting a download shows a green lower-right floating entry. When downloading and launching overlap, the entry prioritizes download progress, while the left navigation can switch to launch logs.
- A window currently showing download progress closes automatically when that download finishes. A window switched to launch logs stays open when tasks end. Launch logs are only available from the task entry after a game launch has started at least once.

---

## 🎮 Offline Launch

- Supports scanning Minecraft home directories and per-version instance directories under `versions/<version>`.
- Provides offline username validation and deterministic UUID generation without reading or relying on online account tokens.
- Supports version inheritance, modern and legacy launch arguments, OS rules, and classpath construction.
- Safely extracts natives and cleans temporary files after the game exits.
- Selects the closest Java runtime that meets the version JSON minimum Java requirement from Minecraft runtime, `NYALAUNCHER_JAVA`, `JAVA_HOME`, or `PATH`; explicit configuration may allow using a higher major version.
- Classpath merging distinguishes normal libraries, `natives-*`, `unsafe`, and other Maven classifiers to avoid overwriting identical LWJGL dependencies.
- The launch page provides directory scanning, version selection, offline username, run status, and exit code information.
- Active launch tasks reuse the global circular task entry in the lower-right corner; clicking it shows the latest 2,000 lines of Java standard output and error logs. The entry hides after launch or download tasks end, while an opened launch-log view stays open after the game process exits.

> Offline accounts cannot join servers that require official authentication; ensure the target version, libraries, and resources are fully installed before launching.

---

## 📦 Tech Stack

| Component       | Technology                     |
|----------------|-------------------------------|
| UI Framework    | Avalonia UI 12.1.1            |
| Runtime         | .NET 10                       |
| Component contract | .NET 10, no Avalonia dependency |

---

## 🔧 Project Structure

| Project                            | Responsibilities                                 |
|-----------------------------------|--------------------------------------------------|
| NyaLauncher.Core                  | Core launch functionality for NyaLauncher        |
| NyaLauncher.Avalonia              | Frontend UI based on Avalonia                    |
| NyaLauncher.Avalonia.Animations   | Animation library for NyaLauncher UI             |
| NyaLauncher.Plugin.Abstractions   | UI-independent contracts, geometry, elements, runtime state, and validation for third-party components |
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

v0.1.0-gp3
- Added the polygon component foundation with custom concave or convex outlines and independent component sizes.
- Added an Avalonia-independent extension contract covering definition building, registration, instance factories, runtime state, and validation.
- Added text, image, button, progress, and dropdown elements. Images can crop by normalized coordinates or exact pixel regions and support absolute local paths, HTTPS sources, pixelated rendering, and runtime replacement; menu rows pass arguments through stable action IDs.
- Component visuals and pointer hit-testing both follow the real polygon outline, preventing transparent corners from triggering hover incorrectly.
- Integrated polygon components with the component catalog and workspace host, including a draggable hexagonal “Download Task Progress” example, while reusing drag-and-drop, free placement, global scaling, hover stacking, and sidebars.
- Rebuilt the legacy account button as a compact account-selector bar using the polygon framework. It shows the current account and Microsoft, offline, or third-party login mode; its menu pins “Add Account” above all saved accounts.
- The account component, Launch page, and Settings page now share one current-account order. Adding or switching accounts persists to `config.json`, and the legacy `account` component ID migrates to the new ID.
- Added a compact Game Instance Selector bar. It shows the current instance and lists every installed version from the selected Minecraft folder in its right-side dropdown. The component and Launch page share and persist their selection, while directory scanning runs in the background.
- Added a separate Version Selection & Management component and page for multiple version folders, version/mod/resource-pack/available-shader details, editable launch settings, and folder shortcuts. Renaming a version physically renames its folder, JSON, and JAR and repairs inheritance references.
- Fixed the lower-right task entry remaining visible with no active task. It now hides after completion, failure, cancellation, or game exit, while an already-open launch log remains available.
- Added explicit per-instance version isolation and fixed duplicate instance-version presentation. Isolated instances consistently use `versions/<current-version>` for the working directory, mods, resource packs, shaders, configuration, and saves, and lists label isolated versus shared-directory instances.
- Fixed third-party-launcher isolated instances not being detected when entered through a Minecraft root. A traceable shared layout resolver now handles common PCL, HMCL, MultiMC/Prism, CurseForge, Modrinth App, and ATLauncher markers and nested content folders before falling back to actual content structure; details also show the detection source, saved worlds, and an Open Saves Folder shortcut.
- Added direct, read-only content management for third-party instance folders with known metadata markers. Direct launch and physical rename are explicitly blocked until native patch metadata conversion is implemented.
- Fixed version details and folder shortcuts retaining a previous instance path. Detail completion now validates the active selection, refresh clears stale data, content shortcuts resolve the current instance again on click, and Windows opens them explicitly through Explorer.
- Completed global and per-instance memory controls with physical-memory-bounded sliders. An Independent Adjustment switch locks instance sliders by default and uses the global result; automatic mode locks the global slider and recalculates a safe limit from available memory before every launch, with the final decision written to the launch log.
- Added separate base Minecraft and exact mod-loader versions to instance details. Settings and instance Java, window size, JVM, and game arguments now live under collapsed Advanced Options; global values persist, instances follow and lock to them by default, and disabling Follow Global enables independent values that are applied at launch.
- Added icons and detailed metadata to instance and content management. Instances use loader-specific fallbacks and prefer third-party modpack artwork; mods, resource packs, shaders, and worlds show names, authors, versions/dates, descriptions, and available icons. JAR/ZIP and `level.dat` scans run in the background, and size-limited archive images are extracted into the content-icon cache.
- Fixed wide or tall modpack artwork being cropped by fill scaling and the quick instance menu showing only text glyphs. Menu images now share the component image safety boundary, preserve the full aspect ratio, and keep selection as a separate corner badge.
- Rebuilt Launch Game with the polygon framework. A short press on any non-interactive part launches the selected instance with the current account, and preparation, running, failure, and exit states update the component live. It shares one launch pipeline with the Launch page to prevent duplicate starts.
- Added a circular task-status entry in the lower-right corner. It appears when game preparation begins and opens live launch logs containing bounded Java standard output, standard error, and the final exit code.
- Completed the actual game-version download and installation path for version metadata, the client, libraries, the asset index, and asset objects, with eight-way concurrency, SHA-1 verification, valid-file reuse, temporary writes, and atomic replacement.
- Integrated downloads into the unified floating task entry and details window with seven stages, overall progress, speed, byte, and file metrics. Downloads take priority over concurrent launches, the left navigation switches to launch logs, download views auto-close on completion, and launch-log views stay open.
- Added a compact Skin & Cape avatar component. Microsoft-authenticated accounts can select a PNG, confirm the Steve/classic or Alex/slim model, upload it, and activate or disable an owned cape. Offline accounts can persist Steve, Alex, Noor, Sunny, Ari, Zuri, Makena, Kai, or Efe as their launcher default.
- Upgraded workspace profiles to version 6 so existing gp3 workspaces receive the avatar, Game Instance Selector, and Version Management components once and replace the legacy `launch` component in place; components deliberately removed afterward stay removed.
- Restored the original interaction model: short presses keep button or menu behavior, while a long press anywhere on the visible component starts dragging; no fixed drag handle is shown or required.
- Fixed the UI freeze during the first offline-skin load and blank avatars when no Minecraft directory is configured. Directory scans, JAR reads, and fallback PNG generation now run as cancelable background work, with all nine textures cached per directory context.
- Fixed Mojang profile texture URLs being discarded when returned as HTTP, incorrect cropping of legacy 64×32 skins, the hat layer covering the face, and the cape dialog failing across UI-thread boundaries. Microsoft avatars now support both 64×32 and 64×64 skins, and owned capes can be selected normally.
- Workspace persistence continues to store only stable component IDs, feature areas, relative coordinates, and stacking order; plugin definitions and transient progress are not persisted.
- Improved configuration-directory migration: existing target configuration can be adopted while the previous configuration is deleted or backed up, followed by a workspace and launch-configuration refresh.
- Standardized the newgui frontend version as `v0.1.0-gp3`; the Core launcher version remains `0.1.0`.

v0.1.0-gp2

> `v0.1.0-gp2` is only the codename for the second v0.1.0 new-GUI preview; it is not written into the Core version.

- Synchronized with the main branch.
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

v0.1.0-pre1
- Split GUI into a separate library (NyaLauncher.Avalonia.Animations).
- Improved several visual stutter issues.
![v0.1.0-pre1 main screen](img/v0.1.0pre1.png)
