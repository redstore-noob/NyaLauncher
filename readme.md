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
- 修复版本继承时 classpath 依赖索引覆盖问题。
- Java 版本要求改为最低版本约束，支持使用更高主版本启动；已验证 Minecraft 1.21.4 可使用 Java 21 与 Java 25。

v0.1.0pre1
- 将用户界面中的GUI拆分成独立库(NyaLauncher.Avalonia.Animations)
- 改善了出现的部分抽搐现象
![v0.1.0pre1主界面截图](img/v0.1.0pre1.png)
