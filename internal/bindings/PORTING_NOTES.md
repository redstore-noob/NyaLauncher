# internal/bindings 移植说明（Wails 绑定层）

12 个 API 结构体的全部导出方法即 Wails 命令；wailsjs 生成路径
`wailsjs/go/bindings/<Struct>/<Method>`。事件经 `wailsruntime.EventsEmit` 推送，
ctx 由 `main.go` 的 `OnStartup` → `API.Startup(ctx)` 注入。

## 事件清单

| 事件名 | 载荷 | 来源 |
|---|---|---|
| `instance:changed` | `instance.GameInstanceSnapshot` | `instance.SubscribeChanged` |
| `launch:changed` | `launch.GameLaunchSnapshot` | `GameLaunchService.OnChanged` |
| `download:progress` | `download.GameDownloadSnapshot` | `GameDownloadService.OnChanged` |
| `download:pauseChanged` | `bool` | `download.OnPauseStateChanged` |
| `download:javaProgress` | `download.JavaRuntimeInstallProgress` | Java 安装进度回调 |
| `download:contentProgress` | `{downloaded, total}` | Mod/资源包/整合包文件下载进度 |
| `auth:deviceCode` | `auth.DeviceCodeInfo` | Microsoft 设备码登录 |
| `modpack:exportProgress` | `modpack.ModpackExportProgress` | 整合包导出 |
| `config:profilesChanged` | 无 | `config.AddChangedHandler`（实例档案/目录变更） |
| `music:stateChanged` / `music:trackChanged` / `music:playbackModeChanged` / `music:trackFinished` | 状态 / 曲目 / 模式 / 无 | `music.Shared` 回调 |
| `music:play` `{filePath}` / `music:pause` / `music:resume` / `music:stop` / `music:seek` `{positionMs}` / `music:volume` `{percent}` | — | `audioBridge`（前端 Web Audio 执行实际播放） |

游戏进程输出**没有逐行事件**：`launch.GameLaunchService` 只保留有界内存日志，
前端用 `Launcher.GetLogText()` 配合 `launch:changed` 轮询刷新。

## 音乐前端契约

Go 侧只有状态机 + 播放列表（`music.Shared`，AudioPlayer 换成 `audioBridge`）。
前端收到 `music:play` 后用 Web Audio 播放该文件，并以
`Music.ReportPlaybackProgress(positionMs, durationMs)` 回传进度、
`Music.NotifyTrackFinished()` 通知自然播完（触发 Go 侧自动切歌 / 列表播完逻辑）。
Seek 由前端收到 `music:seek` 后自行执行。

## 接线的钩子（wireInstance / wireMusic）

- `download.SetConfigHooks` → `config.GetValue/SetValue`（DownloadSettings 持久化）。
- `download.RefreshInstancesHook` → `instance.Refresh`；`download.SelectInstanceHook` → `instance.Select`。
- `download.ResolveContentDirectoryHook` → `instance.GameVersionIsolationGetContentDirectory(CurrentSnapshot, …)`。
- `download.ResolveInstanceLayoutHook` → `instance.ResolveLayout`。
- `config.DefaultMinecraftDirectoryLocator` 已由 `instance` 包 init 注入，无需重复接线。
- `API.Startup` 中执行 `logs.Init()`、`download.ApplySavedSettings()`、
  首次 `instance.Refresh`（后台 goroutine）。

## launch_bridge.go 可退役

`internal/download/launch_bridge.go` 是 config / instance 未就绪时的占位替代；
其全部钩子现已在 bindings 层接到正式实现。该文件本身**保留未删**（保持编译），
后续可把 download 包内 `ConfigGetValue / RefreshInstances / ResolveContentDirectoryForInstance`
等改为直接依赖 config / instance 包后删除，并同步简化 `wireInstance`。

## 命名偏离点

- 部分命令名按 Go 包函数直译（如 `DownloadFileToInstance`、`InstallModpackToInstance`），
  与 C# 方法名可能略有出入，语义一一对应。
- `Launch` 是阻塞调用直到启动流程结束；Wails 在后台 goroutine 执行命令，不会卡 UI。
- `Music.GetMusicVolume`（持久化值）与 `Music.GetPlaybackPosition/GetPlaybackDuration`
  （前端回传值）分开；音量设置统一走 `SetMusicVolume`。

## 扩展绑定层（新增能力）

- **对话框 / 外部打开（api_system_ext.go，SystemAPI）**：`SelectDirectory`、
  `SelectFile`、`SaveFile`、`OpenInExplorer`、`OpenPath`。Wails v2.15 runtime 的
  对话框调用内部已处理主线程调度，直接传 ctx 即可。
  跨平台差异：`OpenInExplorer` Linux 下无 "选中" 语义，退化为 `xdg-open` 父目录；
  Windows 用 `explorer /select,<path>`（"/select," 与路径分开传参，exec 自动转义，
  含空格路径安全）；`OpenPath` Windows 用 `rundll32 url.dll,FileProtocolHandler`。
- **本地音频流（localfile_handler.go）**：挂载在 main.go 的
  `assetserver.Options.Handler`（内嵌资源未命中时的回退）。路由
  `/localfile?path=<绝对路径>`，扩展名白名单 .mp3/.ogg/.wav/.flac/.m4a，
  `http.ServeFile` 流式返回（支持 Range 拖进度条）。前端：
  `new Audio('/localfile?path=' + encodeURIComponent(p))`。
  安全：仅接受存在文件的绝对路径 + 扩展名白名单；本地单用户桌面应用场景，无远程访问面。
- **删除实例（api_instance_ext.go，InstanceAPI.DeleteInstance）**：internal/instance、
  internal/launch 无现成实例删除入口（launch.TryDeleteDirectory 无校验，不直接暴露），
  在 bindings 层实现：目标必须是 `<gameDirectory>/versions/<instanceID>`（Abs+Clean+Rel
  校验，instanceID 禁含分隔符），通过后 `os.RemoveAll`。
- **取消微软登录（api_account_ext.go，AccountAPI.CancelMicrosoftLogin）**：
  未改动 internal/auth——其 `Authenticate` 已接受 ctx，bindings 层在 LoginMicrosoft
  中用 `context.WithCancel` 包装并记录 cancel 句柄（loginMu/loginCancel 存于
  AccountAPI，见 services.go），Cancel 时 cancel 掉即可中断轮询。

## api_skin_ext.go（皮肤/头像扩展，2026-09）

- GetAvatarUrl / GetSkinUrl：语义对应 C# MinecraftProfileService（正版 Mojang
  session server 档案，textures.minecraft.net http→https 归一化）、
  AuthlibProfileTextureService（Yggdrasil sessionserver base64 textures）、
  OfflineSkinCatalog.ResolveTextureSourceAsync（缓存 PNG → 客户端 jar 提取 →
  生成占位皮肤，Steve 头像素逐格移植）。GetAvatarUrl 额外裁剪 8×8 头部返回
  data URI（C# 由 AsyncImage.CropRect 完成，WebView 无等价物故在 Go 侧裁剪）。
- UploadSkin：对应 MinecraftProfileService.UploadSkinAsync —— Mojang 实际为
  POST multipart（variant + file），非 PUT；含 401/403 强刷重试与
  MinecraftAppearanceEditor.ValidateSkinFile 的校验语义（PNG ≤4MiB，64×64/64×32）。
- SetOfflineSkin：C# 仅支持内置目录 Id（AccountStore.UpdateOfflineSkin）。扩展：
  传入本地 PNG 路径时校验后复制到 appearance-cache/custom-skins，并把复制后的
  路径写入 OfflineSkinId 复用同一持久化字段（internal/auth 未改动）。
- 披风未移植：C# SetActiveCapeAsync（激活/停用已有披风）存在，绑定层暂缺。
- localfile_handler.go 白名单新增 .png（image/png），供 /localfile 流式返回皮肤贴图。
