<template>
  <section class="page">
    <header class="page-head">
      <h1 class="m-0 text-[28px] font-bold text-primary-text">启动器设置</h1>
      <p class="m-0 text-[13px] text-hint-text">实例、目录、Java 与下载配置</p>
    </header>

    <div class="cards" ref="cardsEl">
      <!-- ==================== 游戏设置 ==================== -->
      <SettingsCard v-show="cardVisible.game" ref="cardGame" icon="🎮" title="游戏设置"
                    subtitle="实例行为、游戏目录与内存分配">
        <template #actions>
          <Button variant="link" size="sm" @click="openInstanceManager">实例管理 ▸</Button>
        </template>

        <SettingRow title="版本隔离"
                    hint="开启后新实例默认使用独立内容目录（存档、Mod、资源包、光影等各自隔离）">
          <NyaToggle :model-value="isolation" @change="onIsolationChange" />
          <template #extra-hint>
            <span class="text-[11px] text-primary">{{ isolationHint }}</span>
          </template>
        </SettingRow>

        <SettingRow title="启动前校验文件完整性"
                    hint="每次启动游戏前检查关键文件是否齐全，缺失时自动补全下载">
          <NyaToggle :model-value="verifyFiles" @change="onVerifyFilesChange" />
        </SettingRow>

        <!-- 游戏目录 -->
        <div class="field-block" data-title="游戏目录">
          <PathListPanel label="游戏目录" :items="gameDirItems" v-model:selected-index="selectedGameDir"
                         head-hint="「添加目录」填写 Minecraft 根目录路径；「设为当前」切换使用的目录">
            <Button size="sm" @click="addGameDirectory">添加目录</Button>
            <Button variant="secondary" size="sm" :disabled="!canSetCurrentGameDir" @click="setCurrentGameDir">设为当前</Button>
            <Button variant="secondary" size="sm" :disabled="!canRemoveGameDir" @click="removeGameDirectory">删除选中</Button>
            <template #hint><span>{{ gameDirHint }}</span></template>
          </PathListPanel>
          <Input v-model="newGameDirPath" placeholder="输入 Minecraft 根目录绝对路径后点击「添加目录」" />
        </div>

        <!-- 内存 -->
        <div class="field-block" data-title="全局最大内存 内存 自动内存">
          <NyaSlider v-model="memoryMb" :min="512" :max="memorySliderMax" :step="256"
                     label="全局最大内存" show-badge :badge-text="formatMemory(memoryMb)"
                     :disabled="memoryAuto" @change="onMemoryChange">
            <span>{{ memoryRangeText }}</span>
          </NyaSlider>
          <SettingRow title="启动时根据可用内存自动调整">
            <NyaToggle :model-value="memoryAuto" @change="onMemoryAutoChange" />
            <template #extra-hint><span class="text-[11px] text-hint-text">{{ memoryHint }}</span></template>
          </SettingRow>
        </div>
      </SettingsCard>

      <!-- ==================== Java ==================== -->
      <SettingsCard v-show="cardVisible.java" icon="☕" title="Java 运行环境" subtitle="Java 路径管理与启动参数">
        <template #actions>
          <Button variant="link" size="sm" disabled title="待接入：JavaRuntimeLocator 系统扫描（未移植）">
            自动检索全部
          </Button>
        </template>

        <div class="field-block" data-title="Java java jvm 虚拟机 路径 参数 运行时">
          <PathListPanel label="已保存的 Java" :items="javaItems" v-model:selected-index="selectedJava"
                         head-hint="「添加 Java…」填写 javaw.exe 路径（自动探测版本）；列表第一条为默认">
            <Button size="sm" @click="addJava">添加 Java…</Button>
            <Button variant="secondary" size="sm" :disabled="!canSetDefaultJava" @click="setDefaultJava">设为默认</Button>
            <Button variant="secondary" size="sm" :disabled="selectedJava < 0" @click="removeJava">删除选中</Button>
            <template #hint><span>{{ javaHint }}</span></template>
          </PathListPanel>
          <Input v-model="newJavaPath" placeholder="输入 Java 可执行文件绝对路径（如 C:\Program Files\Java\...\javaw.exe）后点击「添加 Java…」" />
        </div>

        <div class="field-block" data-title="jvm 参数 虚拟机">
          <span class="text-[14px] font-semibold text-secondary-text">额外 JVM 参数</span>
          <span class="text-[11px] text-hint-text">每行一个参数，如 -Dfml.ignorePatchDiscrepancies=true</span>
          <textarea v-model="jvmArgsText" rows="3"
                    class="jvm-area w-full rounded-md border border-input bg-background px-3 py-2 text-[12px] text-foreground transition-[border-color,box-shadow] duration-150 placeholder:text-placeholder-text focus-visible:border-ring focus-visible:outline-none focus-visible:ring-[2px] focus-visible:ring-ring/40"
                    placeholder="-XX:+UseG1GC&#10;-XX:MaxGCPauseMillis=50" />
          <Button size="sm" @click="saveJvmArgs">保存 JVM 参数</Button>
        </div>

        <SettingRow title="Java 运行时下载"
                    hint="自动下载 Temurin JDK（Java 8 / 17 / 21）到 .minecraft/runtime，启动时自动检测">
          <Button size="sm" @click="openJavaRuntime">前往下载中心 ›</Button>
          <template #extra-hint><span class="text-[11px] text-hint-text">{{ javaRuntimeText }}</span></template>
        </SettingRow>
      </SettingsCard>

      <!-- ==================== 下载设置 ==================== -->
      <SettingsCard v-show="cardVisible.download" icon="⬇" title="下载设置" subtitle="下载源选择与并发下载配置">
        <div class="field-block" data-title="下载 下载源 镜像">
          <div class="field-row">
            <span class="text-[14px] font-semibold text-secondary-text">下载源</span>
            <div class="field-row-controls">
              <NyaSelect v-model="activeSource" :options="sourceOptions" @change="onActiveSourceChange" />
              <Button variant="secondary" size="sm" disabled title="待接入：DownloadSourceProvider.MeasureLatencyAsync（未移植）">
                测速
              </Button>
            </div>
          </div>
        </div>

        <div class="field-block" data-title="回退 下载源">
          <div class="field-row">
            <span class="text-[14px] font-semibold text-secondary-text">自动回退源</span>
            <NyaSelect v-model="fallbackSource" :options="fallbackOptions" @change="onFallbackChange" />
          </div>
          <span class="text-[11px] text-hint-text">{{ downloadSourceHint }}</span>
        </div>

        <div class="field-block" data-title="并发 线程 下载">
          <NyaSlider v-model="parallelDownloads" :min="1" :max="32" :step="1"
                     label="并行下载线程数" show-badge :badge-text="String(parallelDownloads)"
                     @change="onParallelChange">
            <span>同时下载的文件数量，网络较好时可适当增大</span>
          </NyaSlider>
        </div>
      </SettingsCard>

      <!-- ==================== 账户入口 ==================== -->
      <SettingsCard v-show="cardVisible.account" icon="👤" title="账户管理"
                    subtitle="管理正版与离线账号、切换默认账号并编辑玩家外观">
        <template #actions>
          <Button size="sm" @click="openAccount">打开账户管理</Button>
        </template>
      </SettingsCard>

      <!-- ==================== 快捷键（原 LauncherSettingsPage） ==================== -->
      <SettingsCard v-show="cardVisible.hotkeys" icon="⌨" title="快捷键"
                    subtitle="应用内操作快捷键，录制时需包含 Ctrl 或 Alt">
        <SettingRow title="打开设置"
                    hint="在启动器任意界面按下即可打开设置页。点击右侧按钮录制新组合键。">
          <Button variant="link" size="sm" class="min-w-[110px]" @click="startHotkeyCapture('openSettings')">
            {{ hotkeys.openSettings || '未设置' }}
          </Button>
          <template #extra-hint><span v-if="hotkeyHint.openSettings" class="text-[11px] text-hint-text">{{ hotkeyHint.openSettings }}</span></template>
        </SettingRow>

        <SettingRow title="快捷启动"
                    hint="以当前选中的实例与账户直接启动游戏（需先在启动页选好版本）。默认未设置。">
          <Button v-if="hotkeys.quickLaunch" variant="secondary" size="sm" @click="clearHotkey('quickLaunch')">清除</Button>
          <Button variant="link" size="sm" class="min-w-[110px]" @click="startHotkeyCapture('quickLaunch')">
            {{ hotkeys.quickLaunch || '未设置' }}
          </Button>
          <template #extra-hint><span v-if="hotkeyHint.quickLaunch" class="text-[11px] text-hint-text">{{ hotkeyHint.quickLaunch }}</span></template>
        </SettingRow>
      </SettingsCard>
    </div>
  </section>
</template>

<script setup>
/*
 * LauncherSettingsView —— 还原 SettingsPage（游戏/Java/下载/账户）+ LauncherSettingsPage（快捷键）。
 * 真实绑定：ConfigAPI（隔离/校验/目录/Java/JVM 参数）、LauncherAPI（内存 6 命令 + DetectJavaMajorVersion）、
 * DownloadAPI（下载源 7 命令）、MonitorAPI.GetMemorySnapshot（内存提示）。
 * 添加 Java / 游戏目录支持系统文件/目录选择对话框（SystemAPI.SelectFile/SelectDirectory）；
 * 输入框留空时点「添加」弹出对话框，填写路径则手动添加。
 * 待接入（见 internal/bindings/PORTING_NOTES.md 与本文件底部 PORTING_NOTES）：
 * JavaRuntimeLocator 自动检索、下载源测速。
 */
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import { SettingsCard, SettingRow, NyaToggle, NyaSelect, NyaSlider, PathListPanel } from '../../components/settings/index.js';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import * as Config from '../../../wailsjs/go/bindings/ConfigAPI.js';
import * as Launcher from '../../../wailsjs/go/bindings/LauncherAPI.js';
import * as Download from '../../../wailsjs/go/bindings/DownloadAPI.js';
import { GetMemorySnapshot } from '../../../wailsjs/go/bindings/MonitorAPI.js';
import { SelectDirectory, SelectFile } from '../../../wailsjs/go/bindings/SystemAPI.js';

const router = useRouter();

/* ---------- 搜索（Hub 调用） ---------- */
const cardVisible = reactive({ game: true, java: true, download: true, account: true, hotkeys: true });
const CARD_ALIASES = {
  game: ['游戏设置', '实例', '版本隔离', '游戏目录', '内存', '自动内存', '校验文件'],
  java: ['Java 环境', 'java', 'jvm', '虚拟机', '路径', '参数', '运行时'],
  download: ['下载设置', '下载', '下载源', '镜像', '并发', '线程', '回退'],
  account: ['账户管理', '账号', '登录', '微软', '离线'],
  hotkeys: ['快捷键', '热键', '打开设置', '快速启动', '按键', '录制'],
};
function applySearchFilter(query) {
  const q = (query || '').trim();
  const keys = Object.keys(cardVisible);
  if (!q) {
    keys.forEach((k) => { cardVisible[k] = true; });
    return -1;
  }
  let hits = 0;
  keys.forEach((k) => {
    const matched = CARD_ALIASES[k].some((t) => t.toLowerCase().includes(q.toLowerCase()));
    cardVisible[k] = matched;
    if (matched) hits++;
  });
  return hits;
}

/* ---------- 版本隔离 / 校验文件 ---------- */
const isolation = ref(false);
const verifyFiles = ref(false);
const isolationHint = computed(() => isolation.value
  ? '已开启：未单独配置的实例默认使用版本隔离；检测到其他启动器（PCL/HMCL 等）的隔离布局时跟随该布局。'
  : '已关闭：未单独配置的实例使用共享目录；检测到其他启动器（PCL/HMCL 等）的隔离布局时跟随该布局。');

async function onIsolationChange(v) {
  isolation.value = v;
  await Config.SaveDefaultVersionIsolation(v);
}
async function onVerifyFilesChange(v) {
  verifyFiles.value = v;
  await Config.SaveVerifyFilesBeforeLaunch(v);
}

/* ---------- 游戏目录 ---------- */
const gameFolders = ref([]);
const gameDirectory = ref('');
const selectedGameDir = ref(-1);
const newGameDirPath = ref('');
const gameDirHint = ref('');
const gameDirItems = computed(() => gameFolders.value.map((p) => ({
  text: p,
  badgeText: pathsEqual(p, gameDirectory.value) ? '当前' : '目录',
  badgeHighlight: pathsEqual(p, gameDirectory.value),
})));
const canSetCurrentGameDir = computed(() => {
  const sel = gameFolders.value[selectedGameDir.value];
  return !!sel && !pathsEqual(sel, gameDirectory.value);
});
const canRemoveGameDir = computed(() => selectedGameDir.value >= 0);

function pathsEqual(a, b) {
  if (!a || !b) return false;
  const norm = (p) => p.replace(/[\\/]+$/, '').toLowerCase();
  return norm(a) === norm(b);
}

async function reloadGameDirectories() {
  gameFolders.value = (await Config.GetProfileFolders()) || [];
  gameDirectory.value = (await Config.GetGameDirectory()) || '';
  const cur = gameFolders.value.findIndex((p) => pathsEqual(p, gameDirectory.value));
  selectedGameDir.value = cur; // 默认选中当前目录
  gameDirHint.value = gameDirectory.value
    ? `当前目录：${gameDirectory.value}（共 ${gameFolders.value.length} 个已添加目录）。`
    : '尚未设置当前游戏目录。';
}

async function addGameDirectory() {
  // 输入框有内容时手动添加；否则弹出系统目录选择对话框
  let path = newGameDirPath.value.trim();
  if (!path) {
    try {
      path = await SelectDirectory('选择 Minecraft 根目录');
    } catch { /* 用户取消 */ }
    if (!path) return;
  }
  if (!(await Config.AddProfileFolder(path))) {
    gameDirHint.value = '添加失败：该文件夹可能不包含有效的 Minecraft 版本或可识别的实例。';
    return;
  }
  await Config.SaveGameDirectory(path);
  newGameDirPath.value = '';
  await reloadGameDirectories();
}
async function setCurrentGameDir() {
  const sel = gameFolders.value[selectedGameDir.value];
  if (!sel) return;
  await Config.SaveGameDirectory(sel);
  await reloadGameDirectories();
}
async function removeGameDirectory() {
  const sel = gameFolders.value[selectedGameDir.value];
  if (!sel) return;
  if (pathsEqual(sel, gameDirectory.value)) {
    gameDirHint.value = '无法移除当前正在使用的目录，请先切换到其他目录。';
    return;
  }
  if (!(await Config.RemoveProfileFolder(sel))) {
    gameDirHint.value = '移除失败。';
    return;
  }
  selectedGameDir.value = -1;
  await reloadGameDirectories();
}

/* ---------- 内存 ---------- */
const memoryMb = ref(4096);
const memorySliderMax = ref(4096);
const memoryAuto = ref(false);
const systemMemory = ref(null);
const memoryMonitor = ref(null);
const memoryRangeText = computed(() => systemMemory.value
  ? `系统总内存 ${formatMemory(systemMemory.value.TotalMemoryMb)} · 可选上限 ${formatMemory(memorySliderMax.value)}`
  : '系统总内存');
const memoryHint = ref('');

function formatMemory(mb) {
  return mb >= 1024 ? `${(mb / 1024).toFixed(2).replace(/\.?0+$/, '')} GiB (${mb} MiB)` : `${mb} MiB`;
}

async function updateMemoryHint() {
  if (!systemMemory.value) return;
  if (memoryAuto.value) {
    const d = await Launcher.GetMemoryDecision(null);
    const pct = d.TotalMemoryMb > 0 ? Math.round((d.MaximumMemoryMb * 100) / d.TotalMemoryMb) : 0;
    memoryHint.value = `可用 ${formatMemory(d.AvailableMemoryMb)} / 总计 ${formatMemory(d.TotalMemoryMb)}` +
      ` → 预计分配 ${formatMemory(d.MaximumMemoryMb)}（${pct}%）` +
      `，为系统保留 ${formatMemory(d.ReservedMemoryMb)}。每次启动前自动重新计算。`;
  } else {
    const pct = systemMemory.value.TotalMemoryMb > 0
      ? Math.round((memoryMb.value * 100) / systemMemory.value.TotalMemoryMb) : 0;
    memoryHint.value = `手动上限 ${formatMemory(memoryMb.value)}（占总内存 ${pct}%）。实例可单独设置更低值。`;
  }
  if (memoryMonitor.value) {
    memoryHint.value += ` 当前：启动器 ${memoryMonitor.value.LauncherMemoryMb} MiB · JVM ${memoryMonitor.value.JvmMemoryMb} MiB（${memoryMonitor.value.JavaProcessCount} 个 Java 进程）。`;
  }
}
async function onMemoryChange() {
  await Launcher.SaveManualMaximumMemoryMb(Math.round(memoryMb.value));
  await updateMemoryHint();
}
async function onMemoryAutoChange(v) {
  memoryAuto.value = v;
  await Launcher.SetAutomaticMemoryAdjustmentEnabled(v);
  await updateMemoryHint();
}

/* ---------- Java ---------- */
const javaPaths = ref([]);
const selectedJava = ref(-1);
const newJavaPath = ref('');
const javaHint = ref('');
const javaItems = computed(() => javaPaths.value.map((item, i) => ({
  text: item.JavaPath,
  badgeText: item.JavaVersion ? `Java ${item.JavaVersion}` : '版本未知',
  badgeHighlight: i === 0,
})));
const canSetDefaultJava = computed(() => {
  const sel = javaPaths.value[selectedJava.value];
  return !!sel && selectedJava.value !== 0;
});

async function reloadJavaList() {
  javaPaths.value = (await Config.GetJavaPaths()) || [];
  if (selectedJava.value >= javaPaths.value.length) selectedJava.value = -1;
  javaHint.value = javaPaths.value.length === 0
    ? '尚未保存 Java 路径，启动时将自动检测。'
    : javaPaths.value.length === 1
      ? `已保存 1 条：${javaPaths.value[0].JavaPath}`
      : `已保存 ${javaPaths.value.length} 条，默认：${javaPaths.value[0].JavaPath}`;
}

async function addJava() {
  // 输入框有内容时手动添加；否则弹出系统文件选择对话框（javaw.exe / java.exe）
  let path = newJavaPath.value.trim();
  if (!path) {
    try {
      path = await SelectFile('选择 Java 可执行文件', 'Java 可执行文件', '*.exe');
    } catch { /* 用户取消 */ }
    if (!path) return;
  }
  let version = 'unknown';
  try {
    const v = await Launcher.DetectJavaMajorVersion(path);
    if (v !== null && v !== undefined) version = String(v);
  } catch { /* 探测失败按 unknown 入库 */ }
  if (await Config.AddJava(path, version)) {
    javaHint.value = version !== 'unknown' ? `已添加 Java ${version}：${path}` : `已添加（未能识别版本）：${path}`;
    newJavaPath.value = '';
    await reloadJavaList();
  } else {
    javaHint.value = '添加 Java 路径失败。';
  }
}
async function setDefaultJava() {
  const sel = javaPaths.value[selectedJava.value];
  if (!sel) return;
  if (!(await Config.SetPrimaryJava(sel.JavaPath))) {
    javaHint.value = '设置默认 Java 失败。';
    return;
  }
  const current = await Config.LoadGlobalLaunchSettings();
  await Config.SaveGlobalLaunchSettings({ ...current, JavaExecutable: '' });
  javaHint.value = `已将 ${sel.JavaPath} 设为默认。`;
  await reloadJavaList();
}
async function removeJava() {
  const sel = javaPaths.value[selectedJava.value];
  if (!sel) return;
  if (await Config.RemoveJava(sel.JavaPath)) {
    javaHint.value = `已移除：${sel.JavaPath}`;
    selectedJava.value = -1;
    await reloadJavaList();
  } else {
    javaHint.value = '移除 Java 路径失败。';
  }
}

/* ---------- JVM 参数 ---------- */
const jvmArgsText = ref('');
async function saveJvmArgs() {
  const args = jvmArgsText.value.split(/\r?\n/).map((s) => s.trim()).filter(Boolean);
  const current = await Config.LoadGlobalLaunchSettings();
  const ok = await Config.SaveGlobalLaunchSettings({ ...current, JavaExecutable: '', AdditionalJvmArguments: args });
  javaHint.value = ok ? 'JVM 参数已保存。' : '保存失败。';
}

/* ---------- Java 运行时 ---------- */
const javaRuntimeText = ref('');
async function reloadJavaRuntimes() {
  try {
    const runtimes = (await Download.GetInstalledJavaRuntimes()) || [];
    javaRuntimeText.value = runtimes.length === 0
      ? '尚未安装自动下载的 Java 运行时。'
      : `已安装：${runtimes.map((r) => `Java ${r.MajorVersion ?? '?'}`).join('、')}`;
  } catch { javaRuntimeText.value = ''; }
}

/* ---------- 下载设置 ---------- */
const sources = ref([]);
const activeSource = ref('');
const fallbackSource = ref('');
const parallelDownloads = ref(8);
const downloadSourceHint = ref('');
const sourceOptions = computed(() => sources.value.map((s) => ({ value: s.Name, label: s.Name })));
const fallbackOptions = computed(() => [
  { value: '__disabled__', label: '禁用回退' },
  ...sources.value.map((s) => ({ value: s.Name, label: s.Name })),
]);

function updateDownloadHint() {
  downloadSourceHint.value = fallbackSource.value && fallbackSource.value !== '__disabled__'
    ? `当前：${activeSource.value}，失败时自动回退到 ${fallbackSource.value}。`
    : `当前：${activeSource.value}，未设置回退源。`;
}
async function onActiveSourceChange(name) {
  const src = sources.value.find((s) => s.Name === name);
  if (src) await Download.SaveActiveDownloadSource(src);
  updateDownloadHint();
}
async function onFallbackChange(name) {
  if (name === '__disabled__') {
    await Download.SaveFallbackDownloadSource(null);
  } else {
    const src = sources.value.find((s) => s.Name === name);
    if (src) await Download.SaveFallbackDownloadSource(src);
  }
  updateDownloadHint();
}
async function onParallelChange() {
  await Download.SaveParallelDownloads(Math.round(parallelDownloads.value));
}

/* ---------- 快捷键（前端捕获 + localStorage 持久化；原 AppHotkeys 前端部分） ---------- */
const HOTKEY_KEY = 'nyalauncher.hotkeys';
const hotkeys = reactive(loadHotkeys());
const hotkeyHint = reactive({ openSettings: '', quickLaunch: '' });
const capturing = ref(null);

function loadHotkeys() {
  try { return JSON.parse(localStorage.getItem(HOTKEY_KEY)) || {}; } catch { return {}; }
}
function persistHotkeys() {
  localStorage.setItem(HOTKEY_KEY, JSON.stringify({ ...hotkeys }));
}
function formatGesture(e) {
  const parts = [];
  if (e.ctrlKey) parts.push('Ctrl');
  if (e.altKey) parts.push('Alt');
  if (e.shiftKey) parts.push('Shift');
  const key = e.key.length === 1 ? e.key.toUpperCase() : e.key;
  if (!['Control', 'Alt', 'Shift'].includes(e.key)) parts.push(key);
  return parts.join('+');
}
function onKeydown(e) {
  if (!capturing.value) return;
  e.preventDefault();
  e.stopPropagation();
  if (e.key === 'Escape') {
    hotkeyHint[capturing.value] = '';
    capturing.value = null;
    return;
  }
  if (!(e.ctrlKey || e.altKey)) {
    hotkeyHint[capturing.value] = '无效组合：需要包含 Ctrl 或 Alt，再试一次（Esc 取消）';
    return;
  }
  hotkeys[capturing.value] = formatGesture(e);
  persistHotkeys();
  hotkeyHint[capturing.value] = '';
  capturing.value = null;
}
function startHotkeyCapture(action) {
  if (capturing.value === action) { capturing.value = null; return; }
  capturing.value = action;
  hotkeyHint[action] = '请按下新组合键（需包含 Ctrl 或 Alt）· Esc 取消 · 再点一次按钮取消';
}
function clearHotkey(action) {
  delete hotkeys[action];
  persistHotkeys();
}

/* ---------- 导航 ---------- */
function openInstanceManager() { router.push('/versions'); }
function openAccount() { router.push('/settings/account'); }
function openJavaRuntime() { router.push('/download'); }

/* ---------- 初始化 ---------- */
onMounted(async () => {
  try {
    isolation.value = await Config.GetDefaultVersionIsolation();
    verifyFiles.value = await Config.GetVerifyFilesBeforeLaunch();
    await reloadGameDirectories();

    memorySliderMax.value = await Launcher.GetMemorySliderMaximum();
    memoryMb.value = await Launcher.GetManualMaximumMemoryMb();
    memoryAuto.value = await Launcher.IsAutomaticMemoryAdjustmentEnabled();
    systemMemory.value = await Launcher.GetSystemMemory();
    GetMemorySnapshot().then((s) => { memoryMonitor.value = s; }).finally(updateMemoryHint);
    await updateMemoryHint();

    const settings = await Config.LoadGlobalLaunchSettings();
    jvmArgsText.value = (settings.AdditionalJvmArguments || []).join('\n');
    await reloadJavaList();
    await reloadJavaRuntimes();

    sources.value = (await Download.GetAllDownloadSources()) || [];
    activeSource.value = await Download.GetActiveDownloadSourceName();
    const fallbackName = await Download.GetFallbackDownloadSourceName();
    fallbackSource.value = fallbackName || '__disabled__';
    parallelDownloads.value = await Download.GetParallelDownloads();
    updateDownloadHint();
  } catch (err) {
    gameDirHint.value = `加载设置失败：${err?.message || err}`;
  }
  window.addEventListener('keydown', onKeydown, true);
});
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown, true));

defineExpose({ applySearchFilter });
</script>

<style scoped>
@import './settings-shared.css';
</style>
