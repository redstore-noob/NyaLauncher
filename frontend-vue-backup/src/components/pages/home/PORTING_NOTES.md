# 组件画布移植笔记（ComponentCanvas.axaml → ComponentCanvas.vue）

移植自 `NyaLauncher.Avalonia/Controls/ComponentCanvas.axaml(.cs)`、
`ComponentLibraryView.axaml.cs`、`MainWindow.axaml.cs`（编辑模式/抽屉逻辑）与
`Framework/WorkspaceProfile.cs`。

## 已移植交互
- 归一化坐标（0~1）绝对定位 + 16px 网格吸附（`SnapGrid=16`）。
- 标题手柄按下拖动移动（`MoveShellLive` 语义：半透明跟随、实时同步相对坐标）。
- 编辑模式：16px 网格线、拖动吸附参考线、右下角等比缩放手柄
  （0.5~3.0，取水平/垂直位移较大者）、右上角旋转手柄（15° 吸附、±180 归一）、
  删除按钮（confirm 后删除，`composables/dialog.js`）。
- 拖动时底部 60x60 垃圾桶（半径 +6px 容差命中），松手删除。
- 组件库抽屉 400px（`ComponentLibraryDrawerWidth=400`），点击添加（默认 0.32/0.30）
  或按住拖入（落点 = 组件中心，`GetRelativeCentered` 语义）；进入编辑模式自动打开。
- 未摆放的已注册组件按三列瀑布流补默认位置（`LoadPlacements` 语义）。
- 动画：入场 220ms 上浮淡入（`SlideFadeInAsync` 等价）、删除 240ms 缩小淡出
  （M3 accelerate）、抽屉 300ms 滑入、组件库卡片错峰 `nya-pop-in`。

## 持久化
Go 移植版暂无 workspace.json 专用 API，摆放存为 config.json 单键 JSON：
`ConfigAPI.SetValue('workspaceComponentPlacements', json)` / `GetValue`（防抖 500ms，
对应原版 `ScheduleWorkspaceProfileSave`）。字段与 `ComponentPlacementProfile` 同构：
`{ componentId, relativeX, relativeY, zIndex, sizeScale, rotationDegrees }`。

## 组件实现状态（10 个原版 + 2 个前端补充）
- 已实现：`game-launch`（账号+版本下拉+启动，主卡）、`account-selector`、
  `game-instance-selector`、`memory-usage`（`MonitorAPI.GetMemorySnapshot` 2s 轮询）、
  `version-manager`（lite：当前版本 + 跳转）。
- 占位卡（复杂交互待移植）：`music-player`、`skin-cape-editor`、`world-launch`、
  `server-join`、`download-task-progress`。
- **前端补充（原版契约层没有）**：`clock`、`text`（任务要求；text 内容存
  localStorage `canvas.text.<id>`）。若后端后续注册同名组件请复用此 Id。

## 其他
- 编辑模式状态：`App.vue` 的 `editMode` ref 经 `provide('homeEditMode', editMode)`
  下发（App.vue 仅 +1 行 provide，按钮逻辑未动）；画布 `inject('homeEditMode')`。
- 未移植：全局组件缩放（`GlobalComponentScale`）、拖拽期间的幽灵飞入垃圾桶动画
  （现为缩小淡出近似）、组件库搜索框、插件组件注册（`ComponentRegistry.Changed`）。
