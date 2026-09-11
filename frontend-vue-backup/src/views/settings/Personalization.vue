<template>
  <section class="page">
    <header class="page-head">
      <h1 class="m-0 text-[28px] font-bold text-primary-text">个性化</h1>
      <p class="m-0 text-[13px] text-hint-text">主题、背景图与工作区布局</p>
    </header>

    <div class="cards">
      <!-- ==================== 外观与主题 ==================== -->
      <SettingsCard v-show="cardVisible.appearance" icon="🎨" title="外观与主题"
                    subtitle="主题风格、明暗模式与自定义背景图">
        <!-- 主题风格：色卡选择器（ThemeStore 热切换） -->
        <div class="field-block" data-title="主题风格">
          <span class="text-[14px] font-semibold text-secondary-text">主题风格</span>
          <div class="flex flex-wrap gap-2">
            <button
              v-for="f in themeStore.families"
              :key="f.id"
              class="flex w-[148px] cursor-pointer flex-col gap-2 rounded-lg border px-3 py-2 text-left transition-colors duration-150 hover:bg-accent"
              :class="themeStore.state.family === f.id
                ? 'border-primary bg-accent'
                : 'border-input bg-muted hover:border-medium-border'"
              @click="themeStore.setFamily(f.id)"
            >
              <span class="h-[5px] rounded-full" :style="{ background: previewBar(f.id) }" />
              <span class="flex items-center justify-between gap-1">
                <span class="text-[13px] font-medium text-primary-text">{{ f.name }}</span>
                <span
                  v-if="themeStore.state.family === f.id"
                  class="flex size-4 items-center justify-center rounded-full bg-primary text-[10px] text-primary-foreground"
                >✓</span>
              </span>
              <span class="text-[10px] text-muted-foreground">{{ f.subtitle }}</span>
            </button>
          </div>
        </div>

        <SettingRow title="明暗模式" data-title="明暗模式">
          <div class="inline-flex">
            <button
              v-for="(m, i) in modes"
              :key="m.value"
              class="cursor-pointer border px-4 py-2 text-[13px] transition-colors duration-150"
              :class="[
                i === 0 ? 'rounded-l-md' : '',
                i === modes.length - 1 ? 'rounded-r-md' : '',
                i > 0 ? '-ml-px' : '',
                themeStore.state.mode === m.value
                  ? 'border-primary bg-primary text-primary-foreground'
                  : 'border-input bg-muted text-body-text hover:border-medium-border hover:bg-accent',
              ]"
              @click="themeStore.setMode(m.value)"
            >{{ m.label }}</button>
          </div>
        </SettingRow>

        <!-- PORTING_NOTES: 彩虹背景（AmbientGradient）/ 星尘特效（SparkleTrail）/ 点击圆环（ClickRing）
             为 Avalonia 附加特效层，前端尚未移植对应渲染层；开关先落 localStorage，特效层接入后消费。 -->
        <SettingRow title="彩虹背景" hint="窗口底部的主题色渐变氛围层（特效层待前端移植）">
          <NyaToggle v-model="fx.ambient" @change="saveFx" />
        </SettingRow>
        <SettingRow title="星尘特效" hint="鼠标移动时的星尘拖尾（特效层待前端移植）">
          <NyaToggle v-model="fx.sparkle" @change="saveFx" />
        </SettingRow>
        <SettingRow title="点击圆环" hint="点击时的扩散圆环动画（特效层待前端移植）">
          <NyaToggle v-model="fx.clickRing" @change="saveFx" />
        </SettingRow>

        <!-- 自定义背景图 -->
        <div class="field-block rounded-lg border border-subtle-border bg-muted px-4 py-3" data-title="自定义背景 壁纸 不透明度 模糊">
          <div class="field-row">
            <div class="field-row-head">
              <span class="text-[14px] font-semibold text-secondary-text">自定义背景图</span>
              <div class="flex items-center gap-2">
                <Button variant="secondary" size="sm" @click="pickBackground">选择图片</Button>
                <Button v-if="backgroundUrl" variant="secondary" size="sm" @click="clearBackground">清除</Button>
              </div>
            </div>
          </div>
          <div class="slider-line">
            <span class="text-[11px] text-muted-foreground">不透明度</span>
            <NyaSlider v-model="customBg.opacity" :min="0.05" :max="0.85" :step="0.01"
                       :disabled="!backgroundUrl" @change="saveCustomBg" />
          </div>
          <div class="slider-line">
            <span class="text-[11px] text-muted-foreground">模糊</span>
            <NyaSlider v-model="customBg.blur" :min="0" :max="30" :step="1"
                       :disabled="!backgroundUrl" @change="saveCustomBg" />
          </div>
          <!-- 页内预览：全局背景层待 App 壳接入（PORTING_NOTES） -->
          <div v-if="backgroundUrl" class="relative h-[120px] overflow-hidden rounded-md bg-surface">
            <img v-if="backgroundUrl" :src="backgroundUrl" alt="" @error="bgPreviewFailed = true"
                 :style="{ opacity: customBg.opacity, filter: `blur(${customBg.blur}px)` }"
                 class="h-full w-full object-cover">
          </div>
        </div>
      </SettingsCard>

      <!-- ==================== 工作区布局 ==================== -->
      <SettingsCard v-show="cardVisible.workspace" icon="▦" title="工作区布局"
                    subtitle="组件尺寸与配置目录；修改后需点击下方「保存配置」生效">
        <SettingRow title="全局组件尺寸"
                    hint="同时缩放组件外框、文字和点击区域"
                    data-title="组件尺寸 缩放">
          <NyaSlider v-model="componentScale" :min="0.65" :max="1.6" :step="0.05" style="width: 260px" />
          <span class="w-[52px] text-right font-mono text-[11px] font-semibold text-primary">{{ Math.round(componentScale * 100) }}%</span>
        </SettingRow>

        <SettingRow title="恢复默认布局" hint="把主界面组件摆放恢复到出厂状态，点击「保存配置」后生效。">
          <Button variant="link" size="sm" @click="resetLayout">恢复默认</Button>
        </SettingRow>

        <div class="flex flex-col gap-2 rounded-lg border border-subtle-border bg-muted px-4 py-3" data-title="配置目录 存储">
          <div class="flex items-center gap-3">
            <span class="text-[14px] font-semibold text-secondary-text">配置目录</span>
            <span class="min-w-0 flex-1 overflow-hidden text-ellipsis whitespace-nowrap font-mono text-[10px] text-subtext-text" :title="storageDir">{{ storageDir }}</span>
          </div>
          <div class="dir-actions">
            <Input v-model="manualStorageDir" class="min-w-[160px] flex-1" placeholder="输入配置目录绝对路径" />
            <Button variant="link" size="sm" @click="applyStorageDir">选择目录…</Button>
            <Button variant="secondary" size="sm" @click="useDefaultStorageDir">平台默认</Button>
          </div>
        </div>

        <div class="save-row">
          <span class="mr-auto text-[11px] text-warning">{{ pendingText }}</span>
          <Button variant="secondary" style="padding: 10px 14px" @click="cancelChanges">放弃修改</Button>
          <Button style="padding: 10px 18px" @click="saveConfig">保存配置</Button>
        </div>
      </SettingsCard>

      <!-- ==================== 实例图标 ==================== -->
      <SettingsCard v-show="cardVisible.icon" icon="🖼" title="实例图标"
                    subtitle="为实例设置自定义封面图标（覆盖默认方块图）">
        <div class="field-block rounded-lg border border-subtle-border bg-muted px-4 py-3" data-title="实例图标 自定义图标">
          <span class="text-[11px] text-hint-text">
            填写游戏目录中的实例（版本）ID 与图标文件路径；图标立即生效并在实例列表显示。
          </span>
          <div class="dir-actions">
            <Input v-model="icon.instanceId" class="min-w-[160px] flex-1" placeholder="实例（版本）ID，如 1.20.4-fabric" />
            <Input v-model="icon.iconPath" class="min-w-[160px] flex-1" placeholder="图标文件绝对路径（png/jpg）" />
            <Button variant="secondary" size="sm" @click="pickIconFile">选择图标…</Button>
          </div>
          <div class="dir-actions">
            <span class="text-[11px] text-hint-text">{{ iconHint }}</span>
            <Button size="sm" :disabled="!icon.instanceId || !icon.iconPath" @click="applyIcon">
              设置图标
            </Button>
            <Button variant="secondary" size="sm" :disabled="!icon.instanceId" @click="removeIcon">移除图标</Button>
          </div>
        </div>
      </SettingsCard>
    </div>
  </section>
</template>

<script setup>
/*
 * PersonalizationSettingsView —— 还原 PersonalizationSettingsPage（外观与主题 / 工作区布局），
 * 并新增「实例图标」管理（ContentAPI 自定义图标 3 命令：GetCustomIconPath/SetCustomIcon/RemoveCustomIcon）。
 * 主题家族/明暗模式走 themeStore（即 ThemeStore 前端实现，热切换）。
 * PORTING_NOTES：
 * - 彩虹背景/星尘特效/点击圆环：特效渲染层未移植，开关暂存 localStorage（key: nyalauncher.fx）。
 * - 自定义背景：经 SystemAPI.SelectFile 选择图片，路径经应用内 /localfile 路由预览
 *   （localStorage key: nyalauncher.customBg；旧版 dataUrl 数据仍兼容显示）；
 *   全局背景层需 App 壳消费（当前仅页内预览）。
 * - 工作区组件缩放：FeatureAreaRegistry 未移植，缩放值暂存 localStorage（key: nyalauncher.componentScale）。
 * - 实例图标：SystemAPI.SelectFile 选择 png 后经 ContentAPI.SetCustomIcon 设置。
 */
import { computed, onMounted, reactive, ref } from 'vue';
import { SettingsCard, SettingRow, NyaToggle, NyaSlider } from '../../components/settings/index.js';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { themeStore } from '../../store/theme.js';
import * as Config from '../../../wailsjs/go/bindings/ConfigAPI.js';
import * as Content from '../../../wailsjs/go/bindings/ContentAPI.js';
import { SelectFile } from '../../../wailsjs/go/bindings/SystemAPI.js';

/* ---------- 搜索（Hub 调用） ---------- */
const cardVisible = reactive({ appearance: true, workspace: true, icon: true });
const CARD_ALIASES = {
  appearance: ['外观与主题', '主题', '个性化', '明暗', '跟随系统', '彩虹', '星尘', '圆环', '背景', '壁纸', '不透明度', '模糊',
    ...themeStore.families.flatMap((f) => [f.name, f.id])],
  workspace: ['工作区布局', '布局', '组件尺寸', '缩放', '默认布局', '配置目录', '存储'],
  icon: ['实例图标', '自定义图标', '封面'],
};
function applySearchFilter(query) {
  const q = (query || '').trim().toLowerCase();
  const keys = Object.keys(cardVisible);
  if (!q) {
    keys.forEach((k) => { cardVisible[k] = true; });
    return -1;
  }
  let hits = 0;
  keys.forEach((k) => {
    const matched = CARD_ALIASES[k].some((t) => t.toLowerCase().includes(q));
    cardVisible[k] = matched;
    if (matched) hits++;
  });
  return hits;
}

/* ---------- 主题 ---------- */
const modes = [
  { value: 'Dark', label: '暗色' },
  { value: 'System', label: '跟随系统' },
  { value: 'Light', label: '浅色' },
];
// 家族预览色条（预览色固定，不随当前主题变化；对应 family.CreatePreviewBrush()）
const PREVIEW_BARS = {
  hatsunemiku: 'linear-gradient(90deg, #39c5bb, #86cecb, #e12885)',
  deepseekpurple: 'linear-gradient(90deg, #4d6bfe, #a06ee1, #f4a7b9)',
  zhishublue: 'linear-gradient(90deg, #002fa7, #3f7fbf, #7ec8e3)',
  mojangred: 'linear-gradient(90deg, #a12722, #d8734b, #6ea55e)',
};
const previewBar = (id) => PREVIEW_BARS[id] || 'linear-gradient(90deg, #888, #ccc)';

/* ---------- 附加特效开关（localStorage） ---------- */
const FX_KEY = 'nyalauncher.fx';
const fx = reactive({ ambient: false, sparkle: false, clickRing: false });
function loadFx() {
  try { Object.assign(fx, JSON.parse(localStorage.getItem(FX_KEY)) || {}); } catch { /* ignore */ }
}
function saveFx() { localStorage.setItem(FX_KEY, JSON.stringify({ ...fx })); notifyFxLayer(); }

/* ---------- 自定义背景（SelectFile 选图 + /localfile 预览，localStorage 持久化） ---------- */
const BG_KEY = 'nyalauncher.customBg';
const customBg = reactive({ path: '', dataUrl: '', opacity: 0.3, blur: 0 });
const bgPreviewFailed = ref(false);
const backgroundUrl = computed(() => {
  if (customBg.dataUrl) return customBg.dataUrl;
  // 经应用内 /localfile 路由预览（Go 侧白名单当前仅含音频扩展名，图片预览失败时自动隐藏）
  return customBg.path && !bgPreviewFailed.value ? `/localfile?path=${encodeURIComponent(customBg.path)}` : '';
});
function loadCustomBg() {
  try { Object.assign(customBg, JSON.parse(localStorage.getItem(BG_KEY)) || {}); } catch { /* ignore */ }
}
function saveCustomBg() { localStorage.setItem(BG_KEY, JSON.stringify({ ...customBg })); notifyFxLayer(); }
/* 特效层（components/overlay/FxLayer.vue）/ 画布缩放（ComponentCanvas.vue）即时跟随 */
function notifyFxLayer() { window.dispatchEvent(new CustomEvent('nya:personalization')); }
async function pickBackground() {
  let path = '';
  try {
    path = await SelectFile('选择背景图片', '图片文件', '*.png;*.jpg;*.jpeg;*.webp;*.bmp');
  } catch { /* 用户取消 */ }
  if (!path) return;
  customBg.path = path;
  customBg.dataUrl = '';
  bgPreviewFailed.value = false;
  saveCustomBg();
}
function clearBackground() {
  customBg.path = '';
  customBg.dataUrl = '';
  saveCustomBg();
}
/* ---------- 工作区布局 ---------- */
const SCALE_KEY = 'nyalauncher.componentScale';
const componentScale = ref(1);
const storageDir = ref('');
const manualStorageDir = ref('');
const pendingText = ref('');
const resetLayoutPending = ref(false);

function loadComponentScale() {
  const v = Number(localStorage.getItem(SCALE_KEY));
  if (!Number.isNaN(v) && v > 0) componentScale.value = Math.min(1.6, Math.max(0.65, v));
}
function resetLayout() {
  resetLayoutPending.value = true;
  componentScale.value = 1;
  pendingText.value = '已标记恢复默认布局，尚未保存';
}
function cancelChanges() {
  resetLayoutPending.value = false;
  loadComponentScale();
  pendingText.value = '';
}
async function applyStorageDir() {
  const dir = manualStorageDir.value.trim();
  if (dir) {
    await Config.SetStorageDirectory(dir);
    storageDir.value = dir;
  }
}
async function useDefaultStorageDir() {
  const def = await Config.GetDefaultStorageDirectory();
  await Config.SetStorageDirectory(def);
  storageDir.value = def;
  manualStorageDir.value = '';
}
async function saveConfig() {
  localStorage.setItem(SCALE_KEY, String(componentScale.value));
  notifyFxLayer();
  resetLayoutPending.value = false;
  pendingText.value = '配置已保存';
  setTimeout(() => { if (pendingText.value === '配置已保存') pendingText.value = ''; }, 2000);
}

/* ---------- 实例图标（ContentAPI） ---------- */
const icon = reactive({ instanceId: '', iconPath: '' });
const iconHint = ref('');
async function pickIconFile() {
  try {
    const path = await SelectFile('选择实例图标', '图片文件', '*.png');
    if (path) icon.iconPath = path;
  } catch { /* 用户取消 */ }
}
async function applyIcon() {
  try {
    const dir = (await Config.GetGameDirectory()) || '.';
    await Content.SetCustomIcon(dir, icon.instanceId, icon.iconPath);
    iconHint.value = `已为 ${icon.instanceId} 设置图标。`;
    icon.iconPath = '';
  } catch (err) {
    iconHint.value = `设置失败：${err?.message || err}`;
  }
}
async function removeIcon() {
  try {
    const dir = (await Config.GetGameDirectory()) || '.';
    const ok = await Content.RemoveCustomIcon(dir, icon.instanceId);
    iconHint.value = ok ? `已移除 ${icon.instanceId} 的自定义图标。` : '该实例没有自定义图标。';
  } catch (err) {
    iconHint.value = `移除失败：${err?.message || err}`;
  }
}

onMounted(async () => {
  loadFx();
  loadCustomBg();
  loadComponentScale();
  try {
    storageDir.value = await Config.GetStorageDirectory();
  } catch { storageDir.value = ''; }
});

defineExpose({ applySearchFilter });
</script>

<style scoped>
@import './settings-shared.css';

/* 换装说明：主题色卡 / 明暗分段按钮 / 子卡 / 输入框均已改为模板内 Tailwind 语义工具类；
   家族预览色条（PREVIEW_BARS）为固定预览色，保留在脚本中。 */
</style>
