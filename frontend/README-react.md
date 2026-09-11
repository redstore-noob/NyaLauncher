# NyaLauncher React 前端（frontend-react/）

React 18 + TypeScript + Vite 7 + Tailwind CSS v4 + shadcn/ui（radix-ui 基座）。
本目录是 Vue 版（`../frontend/`）的整体迁移目标；**阶段一只搭基建 + 窗口壳 + 路由**，
页面为占位组件（标注「待移植」），由后续页面移植 agent 填充。Vue 版在切换完成前继续作为参照与回退，**勿改 `frontend/`**。

## 运行

```bash
cd frontend-react
npm install
npm run dev      # vite 开发服务器（wails dev 暂不接入，后续统一切换）
npm run build    # tsc --noEmit + vite build
```

wails 运行时（`window.runtime`）在纯 vite dev 下不存在——`EventsOn` / 窗口控制等调用会被 catch 掉或静默，属正常；只有 `wails dev` / 打包环境才有桥。

## 目录结构

```
frontend-react/
├── index.html                  # 入口（#root）
├── vite.config.ts              # @ 别名 → src/，tailwindcss v4 Vite 插件
├── tsconfig.json               # strict；wailsjs 不参与 tsc（靠相邻 .d.ts 解析类型）
├── components.json             # shadcn CLI 配置（tsx: true）
├── wailsjs/                    # 从 frontend/wailsjs/ 原样拷贝（框架无关，勿手改）
│   ├── runtime/runtime.js      #   运行时从 window.runtime 取
│   └── go/bindings/*.js|.d.ts  #   后端绑定：wailsjs/go/bindings/<Struct>.js
└── src/
    ├── main.tsx                # 入口：样式按序 themes → shadcn → base → overlay；Theme+Router
    ├── App.tsx                 # 窗口壳（三行 Grid + 导航栏 + 状态栏 + 全局浮层）
    ├── lib/utils.ts            # cn()（clsx + tailwind-merge）
    ├── store/theme.tsx         # ThemeProvider / useTheme（family×mode）
    ├── styles/
    │   ├── themes.css          # 家族主题变量（生成物，与 Vue 版同一份，直接拷贝）
    │   ├── shadcn.css          # Tailwind 入口 + shadcn 语义 ↔ 家族令牌映射 + .nya-pop
    │   ├── base.css            # 间距/圆角/字号/动画令牌 + 通用类（.btn-anim 等）
    │   └── overlay.css         # React 版弹层/页面过渡动画（自 Vue scoped style 平移）
    ├── components/
    │   ├── ui/                 # shadcn React 组件（见下）
    │   └── overlay/            # Icon / FxLayer / NyaAlert / NyaPrompt /
    │                           # DownloadStatusPanel / GameLogOverlay / OverlayHost / state.ts / dialog.ts
    └── views/                  # 页面（占位，待移植）；settings 子页在 views/settings/
```

## 路由（与 Vue 版一致，hash 模式）

`/`（工作区）、`/versions`、`/download`、`/music`、`/modpack`、
`/settings`（壳 `views/settings/Hub.tsx`，index 重定向到 `launcher`）+
`launcher` / `personalization` / `ai` / `about` / `account` 子路由。
页面标题表与返回按钮逻辑在 `App.tsx`（`ROUTE_TITLES`，最长前缀匹配）。
非 `/` 路由自动显示「返回工作区」按钮。

## 主题系统

- `ThemeProvider`（main.tsx 已挂）+ `useTheme()`：`setFamily(id)` / `setMode('Dark'|'Light'|'System')`、
  `families`、`resolvedMode()`。**禁止手写 `document.body.dataset`**。
- localStorage 键 `nyalauncher.theme`，结构与 Vue 版一致（`{family, mode}`，默认 `zhishublue`/`Light`），
  **与 Vue 版互通**（同一浏览器 profile 下切换即共享）。
- 切换即写 `body[data-theme][data-mode]`，CSS 变量级联热生效。
- `System` 模式监听 `prefers-color-scheme: light`。

## 绑定调用方式（与 Vue 版完全一致）

```ts
import { GetConfig } from '../wailsjs/go/bindings/ConfigAPI.js';   // 相对路径按页面层级
import { EventsOn, EventsEmit, EventsOff } from '../wailsjs/runtime/runtime.js';
```

- 路径规则：`wailsjs/go/bindings/<Struct>.js`，方法与 Go 导出方法同名，全部返回 Promise。
- 注意导出名是 `WindowToggleMaximise`（带 Window 前缀）；窗口拖拽用 `style={{ '--wails-draggable': 'drag' } as CSSProperties}`，交互元素设 `noDrag`。
- tsc 不检查 `wailsjs/*.js`（解析走相邻 `.d.ts` / `models.ts`），新增绑定后重新生成并同步两份 wailsjs。

## 全局弹窗（等价 Vue 版 composables/dialog.js）

```ts
import { alert, confirm, promptDialog, showDialogBox } from '@/components/overlay/dialog';
alert('已保存');                                 // 底部警示滑条，severity: info|success|warning|error
alert('失败', { severity: 'error' });
if (await confirm('删除账户', '该操作不可恢复')) { ... }
const name = await promptDialog('新建账户', '请输入名称', { defaultValue: 'Player_01' });
const id = await showDialogBox({ title, message, severity, buttons: [{label:'甲'}, {label:'乙', default:true}] });
```

宿主 `<OverlayHost />` 已在 `App.tsx` 挂载（NyaAlert + NyaPrompt + DownloadStatusPanel），
页面无需自挂。DownloadStatusPanel 监听 `download:progress` / `download:pauseChanged`，
终端态停留 4s 自动收起。toast 用 `import { toast, Toaster } from '@/components/ui/sonner'`（Toaster 已挂根部）。

## shadcn/ui 组件清单（`@/components/ui/`）

button（默认带 `.btn-anim` hover 1.02/按压 0.97 + 点击 `nya-bounce` 三段回弹；`disableBounce` 可关）、
card、dialog、alert-dialog、select、slider、switch、input、tabs、progress、tooltip
（Provider 已在窗口壳包好）、badge、separator、scroll-area、dropdown-menu、sonner。
引用方式：`import { Button } from '@/components/ui/button'`。
Dialog/Select/DropdownMenu/Tooltip 弹层自带 `.nya-pop` 进出场（220ms decelerate 进入 / 150ms accelerate 退出）。

### 颜色令牌速查（同 Vue 版 UI_CONVENTIONS.md）

`bg-background`=`--base-bg`、`bg-primary`=`--accent`（强调色填充）、`bg-secondary`=`--button-bg`、
`bg-muted`=`--control-bg`、`bg-accent`=`--highlight-bg`（hover 弱底色，**不是**强调色）、
`text-foreground`=`--primary-text`、`text-muted-foreground`=`--muted-text`、
`bg-destructive`=`--error`、`border-border`=`--medium-border`、`ring-ring`=`--accent`。
扩展色：`accent-bright/dark/light`、`success/warning/info`、`overlay`、`badge`、`surface`、
`card-border`、`subtle/default/medium/strong/emphasized-border`、
`primary/secondary/body/subtext/hint/muted/placeholder-text`。完整对照表见 `frontend/UI_CONVENTIONS.md`。

## 动画令牌

全部在 `src/styles/base.css`（与 Vue 版同一份，未改动）：缓动 `--ease-emphasized*`、
时长 `--dur-medium 300ms` / `--dur-large 400ms`、`nya-bounce` 三段回弹、
`.btn-anim` / `.card` / `.subcard` / `.list-item` / `.window-button(.close)` / `.nav-back-button` /
`.add-component-button(.edit-active)`、页面过渡 `.page-enter-*`。
弹层动画 `.nya-pop` 在 shadcn.css；React 版弹层（NyaAlert/DownloadStatusPanel/GameLogOverlay/页面路由）
的进出场类在 `src/styles/overlay.css`（`.nya-alert-enter/leave`、`.nya-dlpanel-*`、`.gamelog-*`、`.page-route-enter`）。

**页面切换过渡**：Vue 版 `<transition name="page" mode="out-in">` 在 React 无等价内置实现，
当前为 key 重挂载触发入场动画（300ms 淡入 + 上浮 24px）。如需完整 out-in 双向过渡，
引入 `react-transition-group` 的 `<SwitchTransition>` + `<CSSTransition>` 按同参数配置。

## 给页面移植 agent 的注意事项

1. **事件订阅必须清理**：Vue `onMounted` 里的 `EventsOn` 对应 `useEffect(() => { EventsOn(...); return () => EventsOff('事件名') }, [])`（下载页等高频事件页面务必清理，避免重复订阅）。返回值取消订阅也可：`const off = EventsOn(...); return off`。
2. **无 `.sync` / `v-model`**：受控输入用 `value + onChange`；子组件回调改父状态用 `props.onXxx`（或 prop 名 `onClose` 等）。`watch` 对应 `useEffect`（注意依赖数组，`state` 里可变共享量放 `useRef`）。
3. **无 reactive/computed**：派生值直接在渲染函数里算（便宜）或 `useMemo`（贵）。全局可变状态参考 `components/overlay/state.ts` 的 `useSyncExternalStore` 模式。
4. **provide/inject** → React Context（主题见 `store/theme.tsx`；主页编辑模式目前用 `App.tsx` 的 state，如需 inject 等价请自建 Context）。
5. **scoped style** → 平移进 `src/styles/overlay.css` 或组件级 CSS（类名加前缀防冲突）；keyframes 不要重名。
6. `onMounted` 取数 → `useEffect(..., [])`；错误统一走 `alert(...)`（dialog.ts）。
7. `Teleport to body` → radix 组件自带 Portal；自绘遮罩用 `createPortal(document.body)`。
8. `nextTick(focus)` → `useEffect` 里 `setTimeout(focus, 0~30)`（radix content 挂载后）。
9. 图标：轻量 Material path 图标用 `@/components/overlay/Icon`（与 Vue 版同名 name）；通用图标用 `lucide-react`。两者都可。
10. 颜色/间距一律用语义令牌工具类或 CSS 变量，**不得硬编码**（对照表见上 / UI_CONVENTIONS.md）。
11. `frontend/wailsjs/` 更新绑定后记得同步拷贝到 `frontend-react/wailsjs/`。
