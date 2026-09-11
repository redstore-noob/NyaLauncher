# NyaLauncher 前端 UI 约定

Wails v2 + Vue 3 前端约定，页面移植时必须遵守。

**当前 UI 设计语言：shadcn-vue（reka-ui 基座）+ Tailwind CSS v4。** 页面/新控件
优先使用 `src/components/ui/` 的 shadcn 组件与 Tailwind 工具类；颜色一律引用语义
令牌（下文对照表），不得硬编码。原版 Avalonia 动画系统（弹性回弹 / hover·按压透明度 /
页面切换过渡）全部保留，动画令牌仍在 `src/styles/base.css`。

颜色变量全部由 `scripts/gen-themes.mjs` 从 Avalonia 主题 AXAML 生成（产物
`src/styles/themes.css`、`src/theme/families.js` 已提交，前端运行时不依赖 AXAML；
AXAML 变更后运行 `npm run gen:themes` 重新生成）。

## 0. shadcn-vue / Tailwind 基建

- 依赖：`tailwindcss@4` + `@tailwindcss/vite`（Vite 插件，`vite.config.js` 已接，
  `@` 别名指向 `src/`）、`reka-ui`（无障碍基座）、`class-variance-authority` /
  `clsx` / `tailwind-merge`（`@/lib/utils` 的 `cn()`）、`@lucide/vue`（图标）、
  `vue-sonner`（toast）、`tw-animate-css`。
- 入口：`src/main.js` 按序引入 `themes.css → shadcn.css → base.css`。
- `src/styles/shadcn.css`：Tailwind 入口 + shadcn 语义变量 ↔ 家族令牌映射 +
  旧命名兼容别名 + 弹层动画 `.nya-pop`。shadcn-vue CLI 配置在 `components.json`
  （TypeScript 关闭，组件保持 JS SFC；CLI 拉不动就手写等价 SFC）。
- **工具类色名**：`bg-primary`（= 强调色填充）、`bg-secondary`（= ButtonBg）、
  `bg-muted`（= ControlBg）、`bg-accent`（= HighlightBg，列表/hover 弱底色，
  **不是**主题强调色）、`bg-card` / `bg-popover`（= DialogBg）、`bg-background`
  （= BaseBg）、`text-foreground`（= PrimaryText）、`text-muted-foreground`
  （= MutedText）、`bg-destructive`（= Error）、`border-border`（= MediumBorder）、
  `border-input`（= DefaultBorder）、`ring-ring`（= Accent）。
  另有直通家族令牌的扩展色：`accent-bright/dark/light`、`success/warning/info`、
  `overlay`、`badge`、`surface`、`card-border`、`subtle/default/medium/strong/
  emphasized-border`、`primary/secondary/body/subtext/hint/muted/placeholder-text`。
- 注意：`bg-*` 等 Tailwind 工具类在 @layer utilities 内，`base.css` 的旧通用类
  （`.card`/`.btn` 等，未分层）优先级更高——旧类可覆盖工具类微调，反之不行；
  新页面不要混用以免踩坑。

### shadcn 语义变量 ↔ 旧 --xxx 令牌对照表（页面 agent 迁移用）

| shadcn 变量 / 工具类 | 家族令牌（themes.css） | 典型用途 |
|---|---|---|
| `--background` / `bg-background` | `--base-bg` | 窗口/页面底色 |
| `--foreground` / `text-foreground` | `--primary-text` | 主文字 |
| `--card` / `bg-card` | `--card-bg` | 卡片底色 |
| `--popover` / `bg-popover` | `--dialog-bg` | 弹层/菜单/tooltip 底色 |
| `--primary` / `bg-primary text-primary` | `--accent` | 强调色填充/强调文字 |
| `--primary-foreground` | `--accent-text-color`（暗用 bright/亮用 dark） | 强调色上的文字 |
| `--secondary` / `bg-secondary` | `--button-bg` | 标准按钮底 |
| `--muted` / `bg-muted` | `--control-bg` | 控件底/轨道 |
| `--muted-foreground` / `text-muted-foreground` | `--muted-text` | 次要文字 |
| `--accent` / `bg-accent` | `--highlight-bg` | hover/选中弱底色（注意与强调色区分） |
| `--destructive` / `bg-destructive` | `--error` | 危险操作 |
| `--border` / `border-border` | `--medium-border` | 常规边框 |
| `--input` / `border-input` | `--default-border` | 输入框边框 |
| `--ring` / `ring-ring` | `--accent` | 聚焦环 |
| 圆角 `rounded-sm/md/lg/xl/2xl` | `--radius-xs`(6)/`(md,9)`/`(lg,10)`/`(xl,16)`/`(2xl,20)` | 已重定义对齐原体系 |

shadcn 原名 CSS 变量（`var(--background)` 等）在 body 上也可直接用（vue-sonner
等第三方库场景）；唯独 `--accent` 与家族强调色重名，shadcn 语义未注册该裸变量，
弱底色一律走 `bg-accent` 工具类或 `var(--highlight-bg)`。

### 旧命名兼容别名

themes.css 生成时去掉了 `Color` 后缀（`CardBgColor` → `--card-bg`），而 base.css
与既有页面写的是 `--xxx-color`。`shadcn.css` 已在 body 上注册全套别名
（`--card-bg-color: var(--card-bg)`、`--accent-color: var(--accent)`、
`--accent-bright-color: var(--accent-bright)`（同时修复生成物内
`--accent-text-color` 的断链引用）等），旧页面无需改也能继续跟随主题；
新代码请直接用 shadcn 工具类或 `--xxx` 原名。

### 基础组件库（src/components/ui/，JS SFC，均随主题切换变色）

`Button`（default/secondary/outline/ghost/destructive/link + sm/default/lg/icon；
默认带 `.btn-anim` 弹性动效与点击三段回弹，等同旧 `.btn`/`.btn-accent` 语义）、
`Badge`、`Card`（Card/Header/Title/Description/Content/Footer）、`Dialog`（含
Portal/Overlay/Content/Header/Footer/Title/Description/Close）、`AlertDialog`、
`Select`（Trigger/Content/Item/Group/Label/Separator/Scroll*）、`Slider`、`Switch`、
`Input`、`Tabs`（List/Trigger/Content）、`Progress`、`Tooltip`
（Provider/Trigger/Content，窗口壳已包 TooltipProvider）、`Separator`、
`ScrollArea`（+ScrollBar）、`DropdownMenu`（含 Sub 系）、`Sonner`
（`import { Toaster, toast } from '@/components/ui/sonner'`）。
引用方式：`import { Button } from '@/components/ui/button'`。
Dialog/Select/DropdownMenu 弹层自带 `.nya-pop` 进出场（220ms decelerate 进入 /
150ms accelerate 退出，原版动画令牌）。

## 1. 主题系统

- 结构：`body[data-theme="<family>"][data-mode="dark|light"]` 上的 CSS 变量。
- `data-theme`：`hatsunemiku` / `deepseekpurple` / `zhishublue` / `mojangred`（顺序同 ThemeCatalog.cs）。
- `data-mode`：`dark` / `light`。家族专属键写在 `body[data-theme=x]`，明暗覆盖写在
  `body[data-theme=x][data-mode=y]`；家族未覆盖的键由 `body[data-mode=y]` 的中性基底
  （BasePalette）级联回落。HatsuneMiku 的明暗基底与 BasePalette 相同（0 覆盖键），属正常。
- 切换：只操作 `src/store/theme.js` 的 `themeStore.setFamily(id)` / `setMode('Dark'|'Light'|'System')`，
  禁止手写 `document.body.dataset`。System 模式用 `matchMedia('prefers-color-scheme')` 监听。
- 命名映射：`Color x:Key="CardBgColor2"` → `var(--card-bg2-color)`（去 `Color` 后缀、PascalCase→kebab）。
- 派生键（StyleAlter 运行时派生的强调文字/链接色）：`--accent-text-color`、`--link-text-color`
  （dark 用 accent-bright，light 用 accent-dark）。

## 2. 间距 / 圆角 / 字号令牌（base.css :root）

- 间距（App.axaml 4dp 网格）：`--space-2/4/8/12/16/20/24/32/40`。
- 语义 padding：`--page-margin`(40,40,40,32)、`--card-padding`(24,20)、`--subcard-padding`(16,12)、
  `--listitem-padding`(16,12)。
- 圆角体系：`--radius-xs`6 / `--radius-sm`7 / `--radius-md`9 / `--radius-lg`10 /
  `--radius-xl`16 / `--radius-2xl`20。
- 字号体系：`--font-micro`9 / `--font-caption`10 / `--font-meta`11 / `--font-body`13 /
  `--font-title`15 / `--font-page`21。
- 交互：`--hover-opacity` 0.92、`--pressed-opacity` 0.82（全局按钮/`.interactive` 已生效）。

## 3. 通用类（src/styles/base.css）

| 类 | 用途 | 对应 AXAML |
|---|---|---|
| `.card` | MD3 动作卡/设置卡：CardBg + CardBorder + CardPadding + r20 | SettingsHubPage 大卡 |
| `.subcard` | 子卡/内嵌块：PanelBg + SubtleBorder + SubCardPadding + r10 | 子卡片 |
| `.list-item` | 设置行/列表项：ListItemPadding + hover HighlightBg | 列表行 |
| `.btn` | 标准按钮：ButtonBg + MediumBorder + r9 高34 | 通用 Button |
| `.btn-accent` | 主按钮：Accent 填充 | 主操作 |
| `.btn-anim` | hover 放大 1.02 + 按压 0.97（配 `.btn-bounce` 做三段回弹） | GlobalAnimation/BounceBehavior |
| `.window-button` (+`.close`) | 42×34 r9 窗口控制；close hover 红底白字 | MainWindow.axaml |
| `.nav-back-button` | 返回按钮：高32 r9 SurfaceBg | NavigationBackButton |
| `.add-component-button` (+`.edit-active`) | 编辑模式小钮：高24 字号10 强调边框；激活态 Accent 填充 | AddComponentButton |

旧通用类仍可用（存量页面在用），但**新控件优先用 `src/components/ui/` 的 shadcn
组件 + Tailwind 工具类**；不得硬编码颜色/间距。

## 4. 动效（MaterialMotion.cs 令牌）

- 缓动：`--ease-emphasized` cubic-bezier(0.2,0,0,1)、`--ease-emphasized-decelerate`
  (0.05,0.7,0.1,1)（进入）、`--ease-emphasized-accelerate` (0.3,0,0.8,0.15)（退出）。
- 时长：`--dur-medium` 300ms（页面/弹窗）、`--dur-large` 400ms（布局重排）。
- 页面切换：`<router-view>` 外的 `<transition name="page">` 已配好——入场 300ms
  淡入 + 上浮 24px（fade 前 40% 匀速完成）、出场 180ms 淡出 + 下沉 16px，对应
  `AnimationHelper.SlideFadeIn/OutAsync`。
- 点击回弹 keyframes：`nya-bounce`（0.97→1.06→0.98→1，300ms 三段）。

## 5. Wails 绑定调用方式

- 后端方法（另一 agent 正在生成 `wailsjs/go/bindings/*`）：

  ```js
  import { GetConfig } from '../wailsjs/go/bindings/ConfigAPI/ConfigAPI.js';
  // 路径规则：wailsjs/go/bindings/<Struct>/<Struct>.js，方法与 Go 导出方法同名
  ```

- 绑定结构体（均带 API 后缀，最终以 internal/bindings 生成物为准）：
  Config / Launcher / Download / Account / Instance / World / Content / Modpack /
  Music / Monitor / Server / System。
- 事件：`import { EventsOn, EventsEmit, EventsOff } from '../wailsjs/runtime/runtime.js'`。
  注意导出名是 `WindowToggleMaximise`（带 Window 前缀）；窗口拖拽用
  `style="--wails-draggable:drag"`（交互元素设 `noDrag`）。
- 所有后端调用返回 Promise；页面加载取数放 `onMounted`，错误统一提示（待接 NyaAlert 移植）。

## 6. 路由

`src/router/index.js`，hash 模式：

| 路径 | 视图 | 对应 Avalonia 页面 |
|---|---|---|
| `/` | HomeView（启动面板 GameLaunchPanel） | 工作区 |
| `/versions` | VersionsView | VersionManagerPage |
| `/download` | DownloadView | DownloadPage |
| `/music` | MusicView | MusicPlayerPage |
| `/modpack` | ModpackView | ModpackCreatorPage |
| `/settings` | SettingsView（子路由壳） | SettingsHubPage |
| `/settings/launcher` | LauncherSettingsView | LauncherSettingsPage |
| `/settings/personalization` | PersonalizationSettingsView | PersonalizationSettingsPage |
| `/settings/ai` | AiSettingsView | AiSettingsPage |
| `/settings/about` | AboutView | AboutPage |
| `/settings/account` | AccountView | AccountManagePage |

页面组件放 `src/views/`（settings 子页在 `src/views/settings/`），命名 `<Xxx>View.vue`。
`route.meta.title` 自动显示在标题栏；非 `/` 路由自动显示返回按钮。

## 7. 窗口壳（App.vue）

三行 Grid：标题栏 58px / 内容 1fr / 状态栏 30px；左导航栏 52px（展开 176px，宽度过渡
300ms emphasized）。窗口 1280×760 无边框居中（main.go 配置）。窗口控制按钮调
`WindowMinimise` / `WindowToggleMaximise` / `Quit`；最大化图标跟随 `window:maximised` 事件
（Go 端需 `EventsEmit("window:maximised", bool)`）。
