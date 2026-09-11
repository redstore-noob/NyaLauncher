# 页面移植说明（HomeView / VersionsView / DownloadView）

对应 Avalonia：`MainWindow.axaml` 工作区、`Controls/GameLaunchPanel`、`Controls/ComponentCanvas`、
`Pages/VersionManagerPage(.Actions/.Content)`、`Pages/DownloadPage`。

## HomeView（工作区）

- 布局 = `MainContentHost` 2*/1*：左 `ComponentCanvas.vue`，右 `LaunchPanel.vue`（GameLaunchPanel.axaml 逐块还原：标题徽标、账户卡 r12、版本下拉、AccentDeep 启动按钮 h44 r12、空态/去添加链接）。
- 数据：`AccountAPI.GetAccounts/GetSelectedAccount/SelectAccountByStableKey/MoveAccountToTop`、
  `InstanceAPI.GetCurrentInstanceSnapshot/SelectInstance` + 事件 `instance:changed`、`launch:changed`；
  启动走 `LauncherAPI.Launch`（阻塞式，Wails 后台执行）。
- **ComponentCanvas 为静态骨架**：仅空画布提示 + 归一化坐标摆放占位。编辑模式、拖拽/缩放/旋转、
  组件库抽屉（ComponentLibraryView，宽 0→400 emphasized 过渡）、拖动垃圾桶（60×60 红圈松手删除）
  均未移植（原 ComponentCanvas.axaml.cs 约 1100 行交互系统）。
- 头像仅显示首字母占位：离线皮肤 / 皮肤站 / 正版档案皮肤解析（OfflineSkinCatalog /
  AuthlibProfileTextureService / MinecraftProfileService）未移植，待后端出头像 URL 命令。
- 「内存提示」为新增近似：`LauncherAPI.GetMemoryDecision` 显示系统内存与自动策略上限
  （原版 GameLaunchPanel 本身没有这行，内存策略细节在版本管理页/设置页）。
- 底栏「编辑模式 / 日志 / 设置快捷钮」、GameLogOverlay、TaskActivityButton 属窗口壳（App.vue）范畴，未在此页处理。

## VersionsView（版本管理）

- 已还原：头部（标题 / 文件夹下拉 280×42 / 重新扫描 / 打开游戏文件夹 ▾ 菜单 12 项）、
  左 250px 实例列表 + 计数、右详情卡（空态、图标块、MD3 下划线指示条标签页 ×7）、
  底部状态栏。
- 数据：`ConfigAPI.GetProfileFolders/GetGameDirectory/SaveGameDirectory/GetVersionProfile/SaveVersionProfile`、
  `InstanceAPI.RefreshInstances/SelectInstance/GetVersionDetails/RenameInstance` +
  `instance:changed` / `config:profilesChanged` 事件；内存策略文案用 `LauncherAPI.GetMemoryDecision`
  按原版 Content.cs 四分支近似。
- 差异 / 待办：
  - **打开文件夹不可用**：Wails 绑定层无「打开资源管理器」命令（SystemAPI 只有日志接口），
    菜单点击为空操作，待后端补命令。
  - **删除实例不可用**：InstanceAPI 无 DeleteInstance 绑定，按钮弹提示占位。
  - 实例图标：原版经 `ContentAPI.GetInstanceVisual(versionID, loaderName)` 解析图标文件/
    字形，这里仅回退首字母（图标文件为本地路径，前端展示需要资产 http 或 base64 命令）。
  - 「查看日志」「打开版本/模组/存档文件夹」按钮未渲染（同上，缺后端命令与日志遮罩）。
  - ContentEntryItem/SaveEntryItem 的启停切换、存档备份/导出/删除操作未接
    （`ContentAPI` 的 Read* 已具备，启停 rename 管线待确认命令）。
  - 实例右键菜单（原版 PointerPressed/Released 特殊处理）未移植。

## DownloadView（下载大厅）

- 已还原：标题区（下载大厅 / 进度条 / 当前任务行 / 取消下载 / 刷新）、左侧竖排 MD3 标签 ×6、
  Minecraft 本体（搜索 + 全部/正式/快照/远古筛选 + 50 条/页分页 + 行点击下载）、
  Mod/整合包/光影包/材质包（Modrinth 列表：图标、标题、描述、下载/收藏数、空态 👻）、
  Java（版本建议卡、供应商/实时版本双栏、下载进度、已安装列表「使用此 Java/删除」）。
- 数据：`DownloadAPI.GetVersions/ApplyVersionFilter/StartDownload/CancelDownload/
  GetCurrentDownloadSnapshot/QueryAvailableJavaVersions/InstallJavaRuntime/
  GetInstalledJavaRuntimes/DeleteJavaRuntime`；事件 `download:progress`、`download:javaProgress`。
- **Modrinth 搜索走前端 fetch 直连 api.modrinth.com**：绑定层没有 Modrinth 搜索命令
  （原版 C# 的 `ModrinthSearch.SearchAsync` 是 Core 层直接 HTTP）。一次拉 100 条 +
  客户端关键词过滤 + 50/页分页，与原版行为一致。若要求走后端镜像/代理，需新增绑定。
- 差异 / 待办：
  - ~~内容下载流程未接~~ 已接：MinecraftDownloadOverlay（版本下载确认）与
    ContentDownloadOverlay（Modrinth 版本/目标实例选择 + `download:contentProgress` 进度）
    在 `src/components/overlay/`，DownloadView 条目点击即弹；整合包标签页有「导入本地整合包」
    （SelectFile → ReadModpackRequirements → InstallModpackToInstance）。
  - 遮罩差异（PORTING）：① 原版整合包安装会按 requirements 自动安装缺失的 MC 版本 +
    加载器（ResolveModpackTargetVersionAsync），前端简化为只解压安装并提示版本要求，
    Go 已有 StartModLoaderDownload 可作后续编排；② 原版内容下载可取消（CancellationToken），
    Go 内容下载命令无取消入参，遮罩未提供取消按钮。
  - **版本管理页（VersionsView）**：
    - 模组/资源包/光影启停开关（C# ContentEntryItem = 目录内加/去 `.disabled` 后缀 rename）
      **未接**：ContentAPI/InstanceAPI 绑定层暂无对应命令，前端不自行造 Go；UI 仅展示
      后端已解析的 `GameContentEntry.IsDisabled`，开关置灰待后端补齐（如
      `ToggleContentEnabled(sourcePath)`）。
    - 实例图标走 `ContentAPI.GetInstanceVisual(versionId, loaderName)`，IconPath 为本地
      路径时经 `/localfile?path=` 流返回——**Go localfile_handler 扩展名白名单目前仅含
      音频（.mp3/.ogg/.wav/.flac/.m4a），.png/.jpg 会被 403**，前端已做 onerror 回退字形，
      待白名单扩充图片扩展后自动生效（未改 Go）。
    - 「查看日志」按钮为 GameLogOverlay 的调用点：动态 `import('.../GameLogOverlay.vue')`，
      组件缺失（另一 agent 未完成）时降级 alert 提示。
  - JavaVendor 枚举：后端 `download.JavaVendor` 是数值枚举，前端按固定顺序表传索引，
    顺序需与 Go 枚举核对（javaVendors 数组）。
  - Minecraft 版本条目的 TypeIconSource（版本类型贴图）用 emoji 近似（原版 AsyncImage）。
  - 下载进度条只在「有活动任务」时显示，快照→完成态判断较原版简化。

## 全局注意

- 所有 `context.Context` 参数按生成 d.ts 传 `null`（Wails v2.15 把 ctx 计入入参个数，
  JSON null 反序列化为 nil；运行时行为需在 wails dev 中实测确认）。
- 弹窗：确认/警示一律 `window.alert/confirm` 占位，代码内以 `// TODO: 待接 NyaAlertHost/NyaPromptHost` 标注，
  弹窗组件移植完成后统一替换。
- Material.Icons 图标全部以 emoji / 内联字符近似（▶ 🔍 👻 ⬇ ♥ ⟳ 等），后续可换内联 SVG。
