// ThemeStore（React Context 版）：对应 Avalonia ThemeManager + ThemeCatalog 语义，
// 与 Vue 版 store/theme.js 保持 localStorage 互通（键名 nyalauncher.theme，同结构）。
// - family：主题家族 id（themes.css 的 data-theme 值）
// - mode：'Dark' | 'Light' | 'System'（System 跟随系统偏好并实时监听）
// 切换即在 document.body 上挂 data-theme / data-mode，CSS 变量级联生效（热切换）。
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

export interface ThemeFamily {
  id: string;
  name: string;
  subtitle: string;
}

// 由 scripts/gen-themes.mjs 生成（与 Vue 版 theme/families.js 同源），勿手改。顺序对应 ThemeCatalog.cs。
export const themeFamilies: ThemeFamily[] = [
  { id: 'hatsunemiku', name: '初音未来', subtitle: '初音粉 × 初音青' },
  { id: 'deepseekpurple', name: 'DeepSeek紫', subtitle: '幻紫 × 樱粉语义' },
  { id: 'zhishublue', name: '植树蓝', subtitle: '克莱因蓝 × 晴空语义' },
  { id: 'mojangred', name: 'Mojang红', subtitle: '经典红 × 方块绿' },
];

const STORAGE_KEY = 'nyalauncher.theme';
export const THEME_MODES = ['Dark', 'Light', 'System'] as const;
export type ThemeMode = (typeof THEME_MODES)[number];

interface ThemePrefs {
  family: string;
  mode: ThemeMode;
}

function loadPrefs(): ThemePrefs {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const p = JSON.parse(raw);
      if (themeFamilies.some((f) => f.id === p.family) && THEME_MODES.includes(p.mode))
        return p as ThemePrefs;
    }
  } catch { /* ignore */ }
  return { family: 'zhishublue', mode: 'Light' };
}

const media = window.matchMedia('(prefers-color-scheme: light)');

interface ThemeStore {
  family: string;
  mode: ThemeMode;
  families: ThemeFamily[];
  setFamily(family: string): void;
  setMode(mode: ThemeMode): void;
  resolvedMode(): 'dark' | 'light';
}

const ThemeContext = createContext<ThemeStore | null>(null);

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [prefs, setPrefs] = useState<ThemePrefs>(loadPrefs);

  // System 模式跟随系统偏好
  useEffect(() => {
    const onChange = () => setPrefs((p) => ({ ...p })); // 触发重算 resolvedMode
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, []);

  // 应用 data-theme / data-mode（等价 Vue 版 watchEffect(applyMode)）
  useEffect(() => {
    document.body.dataset.theme = prefs.family;
    document.body.dataset.mode = resolvedModeOf(prefs.mode);
  }, [prefs]);

  // 持久化（等价 Vue 版 watchEffect 写 localStorage）
  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ family: prefs.family, mode: prefs.mode }));
  }, [prefs]);

  const store = useMemo<ThemeStore>(() => ({
    get family() { return prefs.family; },
    get mode() { return prefs.mode; },
    families: themeFamilies,
    setFamily(family) {
      if (themeFamilies.some((f) => f.id === family)) setPrefs((p) => ({ ...p, family }));
    },
    setMode(mode) {
      if (THEME_MODES.includes(mode)) setPrefs((p) => ({ ...p, mode }));
    },
    resolvedMode: () => resolvedModeOf(prefs.mode),
  }), [prefs]);

  return <ThemeContext.Provider value={store}>{children}</ThemeContext.Provider>;
}

function resolvedModeOf(mode: ThemeMode): 'dark' | 'light' {
  return mode === 'System' ? (media.matches ? 'light' : 'dark') : (mode.toLowerCase() as 'dark' | 'light');
}

export function useTheme(): ThemeStore {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used within <ThemeProvider>');
  return ctx;
}
