# 🐱 NyaLauncher

> 一个现代、跨平台的轻量 Minecraft 启动器，为优雅与自由而生。
<br>
![License](https://img.shields.io/badge/license-GPLv3-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10-purple.svg)
![Avalonia](https://img.shields.io/badge/Avalonia-12.0-green.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)

---

## ✨ 简介

**NyaLauncher** 是一款基于 **Avalonia UI 12.0** 与 **.NET 10** 构建的跨平台 Minecraft 启动器。  
它不仅轻量、快速，更注重 **隐私保护** 与 **插件扩展性**，让你在享受游戏的同时，拥有完全自主的控制权。

---

## 🎯 核心特色

- 🚀 **跨平台支持** ——  Windows、macOS、Linux 三端支持，近乎一致的外貌。
- 🔌 **强大插件系统** —— 支持动态加载第三方插件，功能随心扩展。
- 🛡️ **隐私优先** —— 无任何遥测，无第三方追踪，使用独特令牌加密技术，减少通过注册表等漏洞造成的令牌泄露。
- ⚡ **轻量高效** —— 基于 .NET 10 原生 AOT 编译，不增加任何多余功能，全部根据自身需求进行定制。
- 🎨 **现代 UI** —— 基于 Avalonia 的流畅设计，支持自定义。
- ✊ **完全原创** —— 该项目没有借用任何其他优秀启动器的代码，也不会进行刻意的模仿。
---
## 已实现功能与未实现功能
<table>
  <tr>
    <th>功能</th>
    <th>状态</th>
  </tr>
  <tr>
    <td>🚀 跨平台支持</td>
    <td><span>✅ 已完成</span></td>
  </tr>
  <tr>
    <td>⚡️ 流畅动效</td>
    <td><span>✅ 已完成</span></td>
  </tr>
  <tr>
    <td>🎮 离线账号启动</td>
    <td><span>✅ 已完成</span></td>
  </tr>
  <tr>
    <td>🔌 插件系统</td>
    <td><span>🚧 开发中</span></td>
  </tr>
  <tr>
    <td>🛡️ 隐私保护</td>
    <td><span>🚧 开发中</span></td>
  </tr>
  <tr>
    <td>🎭 自定义主题</td>
    <td><span>🚧 开发中</span></td>
  </tr>
  <tr>
    <td>🧩 Mod 管理</td>
    <td><span>🚧 开发中</span></td>
  </tr>
  <tr>
  <td>🎮 联机......?</td>
    <td><span>🤔 后续可能上线，且可能属于可选内容</span></td>
  </tr>
</table>

---

## 🎮 离线启动

- 支持扫描 Minecraft 根目录及 `versions/版本号` 独立实例目录。
- 支持离线用户名校验与稳定 UUID 生成，不读取或依赖在线账号令牌。
- 支持版本继承、现代与旧版启动参数、操作系统规则及 classpath 构建。
- 支持安全解压 natives，并在游戏退出后清理临时文件。
- 根据版本 JSON 的最低 Java 要求，从 Minecraft runtime、`NYALAUNCHER_JAVA`、`JAVA_HOME` 或 `PATH` 自动选择最接近要求的兼容版本；显式配置时允许使用更高主版本。
- classpath 合并会区分普通库、`natives-*`、`unsafe` 等 Maven classifier，避免同名 LWJGL 依赖相互覆盖。
- 启动页提供目录扫描、版本选择、离线用户名、运行状态和退出代码提示。

> 离线账号不能进入要求正版认证的服务器；启动前需确保目标版本、依赖库和资源文件已经完整安装。

---

## 📦 技术栈

| 组件            | 技术选型                        |
|----------------|-------------------------------|
| UI 框架         | Avalonia UI 12.0              |
| 运行时          | .NET 10                       |

---

## 🔧 项目结构

| 项目            | 相关功能                        |
|----------------|-------------------------------|
| NyaLauncher.Core         | 🐱NyaLauncher核心的启动功能集合              |
| NyaLauncher.Avalonia          | NyaLauncher的前端界面，基于Avalonia技术构建                       |
| NyaLauncher.Avalonia.Animations          | NyaLauncher的前端界面动画库，为NyaLauncher所准备      |
| NyaLauncher.MinecraftTokenCrypto    | (**由于某些原因，该库不公开**)关于Minecraft正版账户登录令牌的加密算法      |

---

## 🛠️ 快速开始

### 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本
- 系统运行于Windows10+,MacOS Ventura+,Linux Kernel 5.0+
- 桌面运行时（Windows/macOS/Linux

### 克隆与构建

```bash
git clone https://github.com/your-username/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```
> 再次声明:该项目目前仍处于不完善状态，任何可能出现的使用中/构建时的问题全部在允许的范围内。

---

## 更新日志
近期改动

### v0.1.0-gp2

> `v0.1.0-gp2` 仅作为 v0.1.0 的第二个 new GUI 预览代号，不写入 Core 版本。

- 与main同步更新。
- 重构为可扩展的停靠式工作区，功能区支持上下、左右吸附和自由调整排列。
- 支持拖动功能区接缝调整大小；满足边缘和尺寸条件时可自动缩进为侧边栏。
- 侧边栏支持悬停展开、平滑动画、拖动换边与同边交换，并可通过连续拖动恢复为普通功能区。
- 新增完整个性化配置：可编辑功能区名称、简介、图标与功能按钮，也可新建或删除自定义功能区。
- 功能区布局、尺寸、侧边栏和个性化内容会保存到用户选择的配置目录，并提供跨平台默认目录。
- 功能按钮改为当前窗口内整页跳转，复用启动、资源下载和设置 GUI；个性化已整合进设置，按 `Esc` 可随时进入设置。
- 压缩工作区顶栏和底栏，支持拖动窗口四边与四角调整整体大小；进入功能页面时会覆盖工作区信息栏。
- 清理前端构建缓存和冗余调试符号，同时保留 Windows、macOS、Linux 分平台发布能力。
- 将最新的当前个性化布局、侧边栏与组件位置重新固化为新用户首次启动的默认配置。
- 新增独立“组件库”，支持将组件拖入功能区、跨功能区移动，以及拖回组件库立即删除并保存。
- 功能区现作为独立桌面：组件可自由摆放且不会越界，窗口缩放时按相对位置重排；个性化中可调整全局组件尺寸。
- 组件拖动时显示跟随鼠标的落点虚影，并支持向展开中的侧边栏投放组件。
- 修复有组件时功能区难以缩进侧边栏的问题，并完善横向、纵向缩放阈值判断。
- 完善重叠组件层级：后放组件默认置顶，悬停组件临时高亮并显示在最上层，移开后恢复原有层级。
- 统一窗口最小化、最大化/还原与关闭按钮的矢量图标尺寸、居中方式和状态反馈。

v0.1.0pre1
- 将用户界面中的GUI拆分成独立库(NyaLauncher.Avalonia.Animations)
- 改善了出现的部分抽搐现象
![v0.1.0pre1主界面截图](img/v0.1.0pre1.png)
