/*
 * 组件注册表（对应 Avalonia Framework/Components/ComponentRegistry.cs +
 * Components/*Component.axaml.cs 的 CreateDefinition 元数据）。
 *
 * Id / 标题 / 描述 / 图标 / 首选尺寸 / isPrimary 均与原版逐条对齐，
 * 摆放持久化按 Id 引用，禁止改动已有 Id。
 * clock / text 是本次移植任务要求的前端补充组件（原版契约层没有），已在
 * PORTING_NOTES 标注，后端若补充同名组件请以此处为准对齐。
 */

/** 组件定义清单（注册顺序同 BuiltInComponents.cs，clock/text 追加在尾部）。 */
export const COMPONENT_DEFINITIONS = [
  {
    id: 'game-launch',
    title: '启动游戏',
    description: '一键启动当前选中的游戏实例',
    glyph: '🚀',
    preferredWidth: 300,
    preferredHeight: 120,
    isPrimary: true,
    status: 'implemented', // 快速启动：账号 + 版本下拉 + 启动按钮
  },
  {
    id: 'account-selector',
    title: '账号选择',
    description: '查看与切换当前登录的启动账号',
    glyph: '👤',
    preferredWidth: 280,
    preferredHeight: 96,
    status: 'implemented',
  },
  {
    id: 'game-instance-selector',
    title: '游戏实例选择',
    description: '选择启动使用的游戏版本实例',
    glyph: '🗂️',
    preferredWidth: 300,
    preferredHeight: 96,
    status: 'implemented',
  },
  {
    id: 'skin-cape-editor',
    title: '皮肤与披风',
    description: '查看当前账号皮肤头像并管理账号',
    glyph: '🧑‍🎨',
    preferredWidth: 120,
    preferredHeight: 130,
    // PORTING_NOTE: 皮肤渲染（MinecraftProfileService）未移植，先做账号管理快捷卡
    status: 'implemented-lite',
  },
  {
    id: 'version-manager',
    title: '版本管理',
    description: '查看当前版本并进入版本管理页',
    glyph: '🏷️',
    preferredWidth: 260,
    preferredHeight: 84,
    status: 'implemented-lite', // 显示当前版本 + 点击跳版本管理页
  },
  {
    id: 'world-launch',
    title: '最近的世界',
    description: '查看最近游玩的世界存档',
    glyph: '🌍',
    preferredWidth: 300,
    preferredHeight: 180,
    status: 'implemented', // WorldAPI.GetRecentWorlds + 点击启动所属实例
  },
  {
    id: 'memory-usage',
    title: '内存使用',
    description: '监控启动器与 JVM 内存占用',
    glyph: '💠',
    preferredWidth: 240,
    preferredHeight: 120,
    status: 'implemented', // MonitorAPI.GetMemorySnapshot 2s 轮询
  },
  {
    id: 'server-join',
    title: '服务器快连',
    description: '输入服务器地址快速加入游戏服务器',
    glyph: '🖥️',
    preferredWidth: 420,
    preferredHeight: 100,
    // PORTING_NOTE: LauncherAPI.Launch 暂无进服参数，先做 PingServer 延迟/人数监控 + 启动
    status: 'implemented-lite',
  },
  {
    id: 'music-player',
    title: '音乐控制',
    description: '控制内置音乐播放器的播放',
    glyph: '🎵',
    preferredWidth: 300,
    preferredHeight: 110,
    status: 'implemented', // MusicAPI 播控 + audio.js 进度桥
  },
  {
    id: 'download-task-progress',
    title: '下载任务',
    description: '查看当前下载任务的进度',
    glyph: '⬇️',
    preferredWidth: 260,
    preferredHeight: 100,
    status: 'implemented', // GetCurrentDownloadSnapshot + download:progress 事件
  },
  // ---- 以下两个为前端补充组件（原版契约层没有；见 PORTING_NOTES）----
  {
    id: 'clock',
    title: '时钟',
    description: '显示当前时间',
    glyph: '🕐',
    preferredWidth: 200,
    preferredHeight: 100,
    status: 'implemented', // 前端补充：本地时钟
  },
  {
    id: 'text',
    title: '文本',
    description: '自定义文字便签（双击编辑）',
    glyph: '📝',
    preferredWidth: 220,
    preferredHeight: 100,
    status: 'implemented', // 前端补充：可编辑文本（localStorage 持久化）
  },
]

/** 按 Id 查找定义（对应 ComponentRegistry.Find）。 */
export function findDefinition(id) {
  return COMPONENT_DEFINITIONS.find((d) => d.id === id) ?? null
}

/** 摆放缩放边界（对应 ComponentCanvas.axaml.cs 的 Minimum/MaximumSizeScale）。 */
export const MIN_SIZE_SCALE = 0.5
export const MAX_SIZE_SCALE = 3.0

/** 落位吸附网格（设备无关像素，对应 SnapGrid=16）。 */
export const SNAP_GRID = 16

/** 旋转吸附角度（对应 RotationSnapDegrees=15）。 */
export const ROTATION_SNAP_DEGREES = 15

/** 组件库抽屉宽度（对应 MainWindow.axaml.cs ComponentLibraryDrawerWidth=400）。 */
export const COMPONENT_LIBRARY_DRAWER_WIDTH = 400

/** 标题手柄长按拖动阈值沿用原版「按下即拖」；此处 4px 位移内视为点击。 */
export const DRAG_START_THRESHOLD = 4
