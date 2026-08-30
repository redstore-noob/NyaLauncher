<div align="center">

# 🐱 NyaLauncher

> 一个现代、跨平台的轻量 Minecraft 启动器，为自由而生。
<br>

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Avalonia](https://img.shields.io/badge/Avalonia-12.1.1-67ac09)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![License](https://img.shields.io/badge/license-Apache%202.0-blue)

![CI](https://img.shields.io/github/actions/workflow/status/redstore-noob/NyaLauncher/ci.yml?label=CI&logo=github)
![Release](https://img.shields.io/github/v/release/redstore-noob/NyaLauncher?include_prereleases&label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC&logo=github)
![Stars](https://img.shields.io/github/stars/redstore-noob/NyaLauncher?style=flat&logo=github)
![Issues](https://img.shields.io/github/issues/redstore-noob/NyaLauncher?logo=github)
![Last Commit](https://img.shields.io/github/last-commit/redstore-noob/NyaLauncher?logo=github)
![Repo Size](https://img.shields.io/github/repo-size/redstore-noob/NyaLauncher?label=%E4%BB%93%E5%BA%93%E5%A4%A7%E5%B0%8F)

</div>

---

## ✨ 简介

**NyaLauncher** 是一款基于 **Avalonia UI 12.1.1** 与 **.NET 10** 构建的跨平台 Minecraft 启动器。<br>
它不仅轻量、快速，更注重 **隐私保护** 与 **界面自定义**，让你在享受游戏的同时，拥有完全自主的控制权。<br>
NyaLauncher 是一款自由软件，除了必要状态下保留的库文件之外，所有代码均遵循 [Apache License 2.0](LICENSE)。<br>
启动器不会进行任何用户不知情的遥测，不会侵犯用户任何的隐私，不会对用户作出任何功能限制。

---

## 🛠️ 快速开始

### 🪟🍎🐧 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本
- 系统运行于 Windows 10+ / macOS Ventura+ / Linux Kernel 5.0+
- 桌面运行时（Windows / macOS / Linux）

> 鸿蒙移植计划待定。

### 🔧 克隆与构建

```bash
git clone https://github.com/redstore-noob/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```

---

## 📦 技术栈

| 组件         | 技术选型                 |
|--------------|--------------------------|
| UI 框架      | Avalonia UI 12.1.1       |
| 运行时       | .NET 10                  |
| 组件扩展契约 | .NET 10，不依赖 Avalonia |

---

## 🔧 项目结构

| 项目                             | 相关功能                                                                              |
|----------------------------------|---------------------------------------------------------------------------------------|
| NyaLauncher.Core                 | 🐱 NyaLauncher 核心的启动功能集合                                                     |
| NyaLauncher.Avalonia             | NyaLauncher 的前端界面，基于 Avalonia 技术构建                                        |
| NyaLauncher.Avalonia.Animations  | NyaLauncher 的前端界面动画库，为 NyaLauncher 所准备                                   |
| NyaLauncher.Plugin.Abstractions  | 与 UI 框架无关的组件契约、几何、元素、运行时状态与校验                                |
| NyaLauncher.MinecraftTokenCrypto | **（由于算法不适宜公开原因，该库为闭源库）** 关于 Minecraft 正版账户登录令牌的加密与存储 |

### NyaLauncher.Core 内部结构

| 目录             | 职责                                           |
|------------------|------------------------------------------------|
| `Config/`        | 启动器配置持久化（全局设置、版本配置档案）     |
| `Content/`       | 游戏内容元数据解析（Mod/资源包/光影/存档）     |
| `Download/`      | Minecraft 版本下载与安装                       |
| `Launch/`        | 启动流程核心（实例管理、版本详情、内存策略等） |
| `Launch/Auth/`   | 账号存储与认证模型                             |
| `Models/`        | 共享数据模型                                   |
| `Tools/`         | 通用工具方法                                   |
| `Ai/`            | AI 辅助功能                                    |
| `Logs/`          | 日志系统                                       |

### NyaLauncher.Avalonia 内部结构

| 目录           | 职责                                             |
|----------------|--------------------------------------------------|
| `Controls/`    | 自定义控件（DockWorkspace、InstanceListItem 等） |
| `Converters/`  | 值转换器                                         |
| `Dialogs/`     | 弹窗对话框（皮肤选择、披风选择、配置冲突等）     |
| `Framework/`   | 组件系统框架（功能区、拖拽、多边形组件运行时）   |
| `Pages/`       | 页面（启动页、版本管理、下载、设置等）           |
| `Themes/`      | 主题资源与工具（AXAML 主题字典、画刷、样式切换） |
| `Windows/`     | 独立窗口（个性化、组件库、任务详情）             |

---

## 📚 开发者文档

所有教程已整合到 [`Guides/`](Guides/README.md) 目录：

| 文档                                                 | 内容                                       |
|------------------------------------------------------|--------------------------------------------|
| [项目架构总览](Guides/01-architecture.md)            | 工程划分、启动流程、配置目录、构建与发布   |
| [主题开发指南](Guides/02-theming.md)                 | 资源键全集、热重载原理、创建新主题         |
| [布局与间距规范](Guides/03-layout-and-spacing.md)    | 2px 间距系统与控件规范                     |
| [动画系统指南](Guides/04-animations.md)              | `nya-*` 动效 class、helper API、编写规范   |
| [插件与功能区开发](Guides/05-plugin-development.md)  | 功能区注册、工作区停靠、个性化、持久化     |
| [多边形组件开发](Guides/06-polygon-components.md)    | 组件契约、Builder API、状态与生命周期      |
| [通知框架 NyaNotice](Guides/07-notifications.md)     | `NyaAlert` 与 `NyaPrompt` 的完整 API       |

> 💡 **插件作者**建议按 [架构总览](Guides/01-architecture.md) → [插件开发](Guides/05-plugin-development.md) → [组件开发](Guides/06-polygon-components.md) 的顺序阅读。<br>
> 组件契约工程 `NyaLauncher.Plugin.Abstractions` **不依赖 Avalonia**，目标是 `net10.0`。

---

## 🔃 更新计划

### 📝 版本命名规则

| 版本阶段 | 相关代表                                                                  |
|----------|---------------------------------------------------------------------------|
| beta     | 启动器编写阶段，完全不可用                                                |
| preview  | 启动器测试阶段，已经部分可用但不建议用于日常（当前 v1.0.0-preview1 所处阶段） |
| release  | 启动器正式版本，完全可用状态                                              |
| gp（特殊） | newgui 分支时的特定版本号，对应为主分支 preview                         |

### 🎯 待实现功能

- 插件功能（已成功在下游分支 testplug 验证完毕，并已并入主线）
- 自定义主题（预计将在下一个 preview 版本发布）
- 多语言（实现时间待定）
- AI 辅助翻译/查错（未知）
- 联机（???）

---

## 📈 更新日志

> 📌 近期改动。历史版本已折叠，点击版本标题即可展开。

<details open>
<summary><h3>v1.0.0-preview1</h3></summary>

- **插件系统正式上线（1.0 时代头牌）**：
  - 插件系统自 testplug 分支验证完毕后正式并入主线：插件仓库（浏览 / 下载 / 安装）、插件管理页、运行时宿主与能力授权体系
  - 插件 API 版本化：`v1-p1` → `v1-p2`，主版本 1 内对所有旧插件**向前兼容**，旧插件无需重新编译即可继续运行
  - 兼容垫片：修复旧版插件 DLL 加载时报 `MissingMethodException`（`PolygonComponentTheme` 旧属性签名已完整保留）
  - 新增 `IPluginNotifications`：`NyaAlert` / `NyaPrompt` 通知框架开放给插件（警示条、多按钮对话框、确认框，经 `ui.native` 能力门禁授权后使用）
  - 插件管理页新增插件 API 版本徽章与兼容信息展示
- **设置中心重构**：
  - 设置页拆分为 4 个标签：游戏设置 / 启动器设置 / 工作区设置 / 关于
  - 新增**跨标签设置搜索**：实时过滤、命中数徽章、零命中自动跳转、Esc 一键清空
- **主界面**：
  - 状态栏左下角新增快捷入口抽屉（下载 / 实例管理 / 账号管理 / 插件管理 / 设置），与组件库抽屉互斥联动
- **下载界面**：
  - 版本列表图标化：正式版显示草方块、快照版与远古版各有专属图标（内嵌资源，无网络依赖）
  - 五个标签页（本体 / Mod / 整合包 / 光影 / 材质）切换新增 Material 3 动画
  - 清理列表图标区的 emoji 占位符
- **主界面特效系统**：
  - 自定义主界面背景图：任意本地图片设为工作区半透明背景（自动复制到用户目录防失效），不透明度与高斯模糊（毛玻璃）实时可调，独立开关持久化
  - 星尘特效重写：星星跟随鼠标以「先弯后直」的固定弧线坠下，密度减半更轻盈
  - 新增点击圆环：每次左键点击冒出一圈扩散消散的涟漪，与星尘拆分为两个独立开关，均可在设置页实时切换
  - 彩虹氛围光整体调淡（约四成），氛围感保留、不再喧宾夺主
- **工程与文档**：
  - 版本号单一来源化：标题栏 / 状态栏 / 关于页全部绑定 `NyaLauncherInfo`，发版只改一处
  - 插件示例工程更名为 `NyaLauncher.Avalonia.PluginExamples`
  - README 全面现代化（shields.io 徽章、历史日志折叠）
- 移除了Herobrine

</details>

<details>
<summary><h3>v0.1.0-preview5</h3></summary>

- **主题系统重构**：
  - 主题拆分为「中性基底 `BasePalette` + 家族强调色包 `{家族}_Accents`」，新增主题只需一个强调色文件
  - 强调色阶梯按明暗模式运行时派生（暗底取亮端、亮底取深端），明暗切换实时生效且支持热重载
  - 主题家族新增 / 调整：初音未来（初音粉）、DeepSeek 紫、Mojang 红、植树蓝
- **插件组件系统（Polygon Components）**：
  - 组件统一继承 `PolygonComponentInstanceBase`，声明式 Builder + 状态快照驱动渲染
  - 主界面组件全部组件化：账号选择、皮肤披风、音乐播放器、内存占用、联机、世界启动等
  - 新增组件库抽屉，组件可自由拖拽布局
- **动画系统迁移 Material Design 3**：
  - 一次性动画全部迁移至 Avalonia Transitions（渲染线程驱动），曲线/时长收敛为 MaterialMotion 令牌
  - 微交互幅度优雅化（按压 0.97 / 悬浮 1.02），错峰入场总延迟封顶 360ms
- **全局通知框架**：`NyaPrompt`（居中对话框）与 `NyaAlert`（左下滑入警示条），任意线程可调
- **下载系统**：
  - 新增 Mod / 整合包一键安装（ContentInstallService）、Java 运行时自动下载安装
  - 新增存档管理、Minecraft 服务器在线状态查询
- **账号与正版**：
  - 皮肤/披风管理：皮肤头像合成（脸+帽双层）、离线皮肤目录、自定义账号头像
  - 正版认证加固：令牌被服务端吊销时 401/403 自动静默刷新并重试；账号被封禁（ACCOUNT_SUSPENDED）时给出明确中文提示而非静默失败
- **其他**：
  - 版本隔离判定语义修正（实例显式设置 > 自动检测 > 全局默认 > 共享目录）
  - 图标体系统一为 `material:` 字形协议 + `gameicon:` 内置游戏图标，新增窗口图标
  - 字体回归 Material 内嵌 Roboto，移除内嵌中文字体以精简体积
  - 新增 GitHub Actions 自动编译（推送/PR 编译校验，打 tag 自动三平台发布）
- 已知问题：部分正版账号无法加载档案/皮肤，头像将显示回退图标并弹出提示（官方 403 行为，非启动器问题）
- 移除了Herobrine

</details>

<details>
<summary><h3>v0.1.0-preview4</h3></summary>

- **项目结构大整理**：
  - 从 `Pages/` 中迁移 13 个纯后端服务/模型到 `Core`（GameInstanceStore、GameLaunchService、GameMemorySettings、GameContentMetadataService 等）
  - Avalonia 根目录散落的对话框归入 `Dialogs/`，独立窗口归入 `Windows/`，主题工具归入 `Themes/`
  - 提取实例列表项为独立自定义控件 `InstanceListItem`
  - 所有迁移类型添加了 XML 文档注释
  - `Pages/` 从 30 个文件精简到 18 个（仅保留真正的页面与 UI 相关逻辑）
- 下载界面:新增了Minecraft安装模组加载器的功能，新增了mod下载的功能，新增切换下载源功能
- 主界面:新增音乐播放器组件，玩累了听一会休息吧
- 设置界面:设置界面重制，新增关于选项卡
- 游戏:优化启动时的Java参数，重构了自动内存管理
- 样式:新增切换主题功能，支持浅色/暗色主题，字体切换为启动器内嵌Harmony Sans
- 增加账户管理功能，可以更便捷增删账户
- 启动:新增游戏补全功能
- 替换了部分图标为Material Icons
- 移除了Herobrine

![v0.1.0preview4个性化主题截图](img/v0.1.0preview4-theme.png)
![v0.1.0preview4设置界面截图](img/v0.1.0preview4-settings.png)
![v0.1.0preview4mod下载界面截图](img/v0.1.0preview4-minecraftdownload.png)
![v0.1.0preview4mod界面截图](img/v0.1.0preview4-mod.png)
![v0.1.0preview4关于界面截图](img/v0.1.0preview4-about.png)

</details>

<details>
<summary><h3>v0.1.0-preview3</h3></summary>

- newgui功能完善,已合并回main分支
- 增加了大量组件
- 对Core模块进行了小规模重构(进度:25/100%)
- 增加了Minecraft相关的下载功能
- 增加了Log功能,用于保存启动器/游戏运行时产生的运行文件
- 对于前端的硬编码样式问题进行了修复,其他问题进行了小优化
- 插件系统正在测试中,即将推出第一个可用API
- 后端重复的部分代码/死代码已移除
- animations模块移除部分代码,即将进行重构
- 更改各更新版本命名规则
- 移除了Herobrine

![v0.1.0-preview3主界面截图](img/v0.1.0preview-3-mainwindow.png)
![v0.1.0-preview3游戏管理界面截图](img/v0.1.0preview-3-game.png)
![v0.1.0-preview3账户管理界面截图](img/v0.1.0preview-3-account.png)

</details>

<details>
<summary><h3>v0.1.0-gp2(newgui分支)</h3></summary>

> `v0.1.0-gp2` 仅表示 v0.1.0 newgui 的第二次界面迭代，不写入 Core 版本号。<br>该版本不与main分支相关.

- 对GUI进行了重构(位于newgui分支)，主页变为可更改组件块，增加了自定义自由度(尚不完善，旧版GUI界面保存在main分支)
- 增加了离线启动、正版启动功能
- 对readme.md的bug进行了修复（?）
- 增加了多账户管理
- 增加配置保存功能，配置一次后终于能保留下来了😭
- 对Java搜索进行优化，修复了曾经存在的Java可启动但无法使用问题
- 移除了Herobrine

![v0.1.0-gp2主界面截图](img/v0.1.0pre2-mainwindow.png)
![v0.1.0-gp2启动界面截图](img/v0.1.0pre2.png)
![v0.1.0-gp2设置截图](img/v0.1.0pre2-settings.png)
![v0.1.0-gp2个性化界面截图](img/v0.1.0pre2-settings2.png)

</details>

<details>
<summary><h3>v0.1.0-pre1</h3></summary>

- 将用户界面中的GUI拆分成独立库(NyaLauncher.Avalonia.Animations)
- 改善了出现的部分抽搐现象
- 移除了Herobrine

![v0.1.0-pre1主界面截图](img/v0.1.0pre1.png)

</details>
