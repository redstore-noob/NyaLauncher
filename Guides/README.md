# NyaLauncher 开发者文档中心

> 这里汇总了 NyaLauncher 所有**可扩展子系统**的教程：主题、动画、布局、插件、多边形组件、通知框架。
> 插件作者、主题作者、UI 贡献者都可以从这里出发。

---

## 📚 文档地图

| # | 文档 | 你会学到什么 | 主要受众 |
|---|------|--------------|----------|
| 01 | [项目架构总览](01-architecture.md) | 四个工程的职责与依赖方向、启动流程、配置与数据目录、构建与发布 | 所有人（建议先读） |
| 02 | [主题开发指南](02-theming.md) | 主题家族与明暗变体、全部资源键释义、热重载原理、从零创建新主题 | 主题作者 / UI 贡献者 |
| 03 | [布局与间距规范](03-layout-and-spacing.md) | 2px 基础间距系统、各控件 Padding/Margin 标准、卡片与页面布局 | UI 贡献者 |
| 04 | [动画系统指南](04-animations.md) | 动画模块化约定、`nya-*` 声明式 class、编程式 helper、编写新动画的规范 | UI 贡献者 / 插件作者 |
| 05 | [插件与功能区开发](05-plugin-development.md) | 功能区注册、工作区停靠与侧栏、用户个性化、持久化、跨平台发布 | **插件作者** |
| 06 | [多边形组件开发](06-polygon-components.md) | 与 UI 框架无关的组件契约、Builder API、状态快照、生命周期、校验规则 | **插件作者** |
| 07 | [通知框架 NyaNotice](07-notifications.md) | 警示条 `NyaAlert` 与弹窗 `NyaPrompt` 的完整 API | 插件作者 / UI 贡献者 |
| 08 | [Plugin API V1.1 Bug 修复日志](08-plugin-api-v1.1-bugfixes.md) | 已确认问题的触发条件、根因、修复与兼容性边界 | 插件作者 / 维护者 |

---

## 🧭 按角色选择阅读路径

### 🔌 我想写一个插件
1. [项目架构总览](01-architecture.md) —— 先搞清楚哪些东西能碰、哪些不能碰
2. [插件与功能区开发](05-plugin-development.md) —— 功能区是怎么注册进主界面的
3. [多边形组件开发](06-polygon-components.md) —— 组件契约与 Builder 用法
4. [通知框架](07-notifications.md) —— 用 `NyaAlert` / `NyaPrompt` 与用户交互

> 关键约束：插件契约工程 `NyaLauncher.Plugin.Abstractions` **不引用 Avalonia**，
> 目标是 `net10.0`。你在组件里拿不到 Avalonia 控件，宿主负责渲染。

### 🎨 我想做一套主题
1. [主题开发指南](02-theming.md) —— 资源键全集 + 新建主题步骤
2. [布局与间距规范](03-layout-and-spacing.md) —— 保证你的主题不改崩布局
3. [动画系统指南](04-animations.md) —— 可选，了解主题切换时的过渡处理

### 🖥️ 我想改主界面 / 写页面
1. [项目架构总览](01-architecture.md)
2. [布局与间距规范](03-layout-and-spacing.md)
3. [动画系统指南](04-animations.md)
4. [通知框架](07-notifications.md)

---

## 📖 术语表

文档里反复出现的几个词，先对齐一下：

| 术语 | 英文 | 含义 |
|------|------|------|
| 功能区 | Feature Area | 主界面工作区里的一块可停靠区域，例如「启动中心」「资源与实例」 |
| 动作 | Action | 功能区里的一个按钮型条目，点击执行一段代码 |
| 组件 | Component | 可被拖进功能区的卡片，分传统矩形与多边形两种形态 |
| 多边形组件 | Polygon Component | 用与 UI 框架无关的声明式契约描述的组件，宿主负责渲染 |
| 主题家族 | Theme Family | 一套配色的集合（如 `HatsuneMiku`），内含 Dark / Light 两个变体 |
| 变体 | Variant | 一个家族下的明暗分支，对应 Avalonia 的 `ThemeVariant` |
| 宿主 | Host | 启动器主程序，负责加载插件、渲染组件、管理生命周期 |
| 工作区 | Workspace | 主界面中承载功能区与组件拖拽的可停靠画布 |

---

## ⚙️ 全局约定

### 分层依赖（不可逆向）

```
NyaLauncher.Avalonia            ← 主程序（UI、页面、主题、控件）
   ├── NyaLauncher.Core         ← 业务逻辑（下载/启动/配置/账号），不含 UI
   ├── NyaLauncher.Avalonia.Animations  ← 全部动效实现
   └── NyaLauncher.Plugin.Abstractions  ← 插件契约，不依赖 Avalonia
```

- `Core` 与 `Plugin.Abstractions` **都不知道 Avalonia 的存在**
- 主工程可以引用下层，下层**永远不引用**上层
- 详见 [架构总览](01-architecture.md)

### 动画模块化（硬性规则）

所有动画实现（附加属性、行为、helper、计时器）**必须**写在
`NyaLauncher.Avalonia.Animations/Helpers/` 下。主工程的页面与控件 `.cs`
里禁止直接写动画循环或 `RenderTransform` 逻辑，只能通过：

- `App.axaml` 全局 `Style` 绑定附加属性
- 给元素加 `nya-*` class 触发
- 调用 `Animations` 模块里的静态 helper

详见 [动画系统指南](04-animations.md)。

### 颜色不硬编码

任何 UI 代码里都不要写死颜色。一律引用主题资源键（`{DynamicResource ...Brush}`），
这样主题热重载才能实时生效。详见 [主题开发指南](02-theming.md)。

---

## 🗂️ 文档迁移说明

本目录下的文档由仓库中散落的文件整合而来，原位置已移除：

| 原路径 | 现路径 |
|--------|--------|
| `THEME_REFERENCE.md` | [Guides/02-theming.md](02-theming.md) |
| `NyaLauncher.Avalonia/Themes/Spacing_Guidelines.md` | [Guides/03-layout-and-spacing.md](03-layout-and-spacing.md) |
| `NyaLauncher.Avalonia/Framework/README.md` | 拆分为 [Guides/05-plugin-development.md](05-plugin-development.md) 与 [Guides/06-polygon-components.md](06-polygon-components.md) |
| `PolygonMake_Guide.md` | [Guides/06-polygon-components.md](06-polygon-components.md) |
| `NyaNotice-API.md` | [Guides/07-notifications.md](07-notifications.md) |

---

## ❓ 找不到想要的内容？

- 组件契约的具体类型 → 直接看 `NyaLauncher.Plugin.Abstractions/Components/`，只有 7 个文件
- 内置组件怎么写 → `NyaLauncher.Avalonia/Framework/BuiltIn*Component.cs` 是 8 个活样例
- 主题资源键清单 → `NyaLauncher.Avalonia/Themes/BasePalette.axaml`（中性基底）+ `Themes/HatsuneMiku_Accents.axaml`（家族强调色与背景搭配）是最完整的参考实现
