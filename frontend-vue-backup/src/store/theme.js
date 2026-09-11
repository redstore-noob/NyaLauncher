// ThemeStore：对应 Avalonia 端 ThemeManager + ThemeCatalog 的语义。
// - family：主题家族 id（见 families.js，持久化值与生成 CSS 的 data-theme 一致）
// - mode：'Dark' | 'Light' | 'System'（System 跟随系统偏好并实时监听）
// 切换即在 document.body 上挂 data-theme / data-mode，CSS 变量级联生效（热切换，无需刷新）。
import { reactive, watchEffect } from 'vue';
import { themeFamilies } from '../theme/families.js';

const STORAGE_KEY = 'nyalauncher.theme';

function loadPrefs() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const p = JSON.parse(raw);
      if (themeFamilies.some((f) => f.id === p.family) && ['Dark', 'Light', 'System'].includes(p.mode))
        return p;
    }
  } catch { /* ignore */ }
  return { family: 'zhishublue', mode: 'Light' };
}

const state = reactive(loadPrefs());

const media = window.matchMedia('(prefers-color-scheme: light)');
media.addEventListener('change', () => { if (state.mode === 'System') applyMode(); });

function resolvedMode() {
  return state.mode === 'System' ? (media.matches ? 'light' : 'dark') : state.mode.toLowerCase();
}

function applyMode() {
  document.body.dataset.theme = state.family;
  document.body.dataset.mode = resolvedMode();
}

watchEffect(applyMode);
watchEffect(() => localStorage.setItem(STORAGE_KEY, JSON.stringify({
  family: state.family, mode: state.mode,
})));

export const themeStore = {
  state,
  families: themeFamilies,
  setFamily(family) {
    if (themeFamilies.some((f) => f.id === family)) state.family = family;
  },
  setMode(mode) {
    if (['Dark', 'Light', 'System'].includes(mode)) state.mode = mode;
  },
  resolvedMode,
};
