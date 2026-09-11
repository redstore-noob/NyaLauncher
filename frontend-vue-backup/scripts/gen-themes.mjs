// 主题令牌生成器：解析 Avalonia 主题 AXAML，生成 CSS 变量文件。
// 产物提交进仓库（src/styles/themes.css），前端运行时不依赖 AXAML。
// 用法：node scripts/gen-themes.mjs
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const frontendRoot = join(__dirname, '..');
// Avalonia 工程位于仓库根（NyaLauncher 的上一级）：E:\NyaLauncher\NyaLauncher.Avalonia
const avaloniaThemes = join(frontendRoot, '..', '..', 'NyaLauncher.Avalonia', 'Themes');
const outFile = join(frontendRoot, 'src', 'styles', 'themes.css');

// PascalCase → kebab-case（去掉 Color 后缀）。例：CardBgColor2 → card-bg2
function toCssVar(key) {
  const name = key.replace(/Color$/, '');
  return '--' + name
    .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1-$2')
    .toLowerCase();
}

// 解析一段 axaml 文本里所有 <Color x:Key="X">#YYY</Color>
function parseColors(text) {
  const map = {};
  for (const m of text.matchAll(/<Color\s+x:Key="([^"]+)"\s*>(#[0-9A-Fa-f]+)<\/Color>/g)) {
    map[m[1]] = m[2];
  }
  return map;
}

// 把 axaml 按 ThemeDictionaries 拆为：顶层（模式无关）+ Dark + Light 三段
function splitDictionaries(text) {
  const top = text.replace(/<ResourceDictionary\.ThemeDictionaries>[\s\S]*?<\/ResourceDictionary\.ThemeDictionaries>/, '');
  const pickDict = (key) => {
    const re = new RegExp(`<ResourceDictionary\\s+x:Key="${key}">([\\s\\S]*?)<\\/ResourceDictionary>`);
    return text.match(re)?.[1] ?? '';
  };
  return { top, Dark: pickDict('Dark'), Light: pickDict('Light') };
}

// ---- 1. BasePalette：中性兜底基底（Dark/Light 两套） ----
const baseText = readFileSync(join(avaloniaThemes, 'BasePalette.axaml'), 'utf8');
const baseParts = splitDictionaries(baseText);
const base = {
  global: parseColors(baseParts.top),
  Dark: parseColors(baseParts.Dark),
  Light: parseColors(baseParts.Light),
};

// ---- 2. 主题家族（顺序即 ThemeCatalog.cs 注册顺序） ----
const families = [
  { id: 'deepseekpurple', file: 'DeepSeekPurple_Accents.axaml' },
  { id: 'hatsunemiku', file: 'HatsuneMiku_Accents.axaml' },
  { id: 'zhishublue', file: 'ZhiShuBlue_Accents.axaml' },
  { id: 'mojangred', file: 'MojangRed_Accents.axaml' },
];

const familyData = families.map(({ id, file }) => {
  const parts = splitDictionaries(readFileSync(join(avaloniaThemes, file), 'utf8'));
  return {
    id,
    global: parseColors(parts.top),
    Dark: parseColors(parts.Dark),
    Light: parseColors(parts.Light),
  };
});

// ---- 3. 生成 CSS ----
const lines = [
  '/* 本文件由 scripts/gen-themes.mjs 从 NyaLauncher.Avalonia/Themes/*.axaml 生成，',
  '   勿手改。重新生成：npm run gen:themes */',
  ''];
let counts = { baseDark: 0, baseLight: 0, family: {} };

function emit(selector, colors, indent = '  ') {
  const keys = Object.keys(colors);
  if (!keys.length) return;
  lines.push(`${selector} {`);
  for (const k of keys) lines.push(`${indent}${toCssVar(k)}: ${colors[k]};`);
  lines.push('}', '');
  return keys.length;
}

// 基底：body[data-mode="dark"] / [data-mode="light"]（家族未覆盖时即回落到这里的值）
counts.baseDark = emit('body[data-mode="dark"]', { ...base.global, ...base.Dark }) ?? 0;
counts.baseLight = emit('body[data-mode="light"]', { ...base.global, ...base.Light }) ?? 0;

// 家族：body[data-theme="x"]（模式无关：强调色阶梯/语义色，并派生链接色）
//       + body[data-theme="x"][data-mode="y"] 仅家族专属覆盖键（级联覆盖基底同名变量）
for (const fam of familyData) {
  const nGlobal = emit(`body[data-theme="${fam.id}"]`, fam.global) ?? 0;
  // 派生键：强调文字 / 链接色（对应 StyleAlter 运行时派生的 AccentTextBrush / LinkTextBrush）
  lines.push(`body[data-theme="${fam.id}"][data-mode="dark"] {`);
  lines.push('  --accent-text-color: var(--accent-bright-color);');
  lines.push('  --link-text-color: var(--accent-bright-color);');
  lines.push('}', '');
  lines.push(`body[data-theme="${fam.id}"][data-mode="light"] {`);
  lines.push('  --accent-text-color: var(--accent-dark-color);');
  lines.push('  --link-text-color: var(--accent-dark-color);');
  lines.push('}', '');
  // 家族 Dark/Light 字典里与基底不同的键 → 覆盖块
  const ov = {};
  for (const mode of ['Dark', 'Light']) {
    ov[mode] = {};
    for (const [k, v] of Object.entries(fam[mode])) {
      if (base[mode][k] !== v) ov[mode][k] = v;
    }
  }
  const nDark = emit(`body[data-theme="${fam.id}"][data-mode="dark"]`, ov.Dark) ?? 0;
  const nLight = emit(`body[data-theme="${fam.id}"][data-mode="light"]`, ov.Light) ?? 0;
  counts.family[fam.id] = { global: nGlobal, darkOverrides: nDark, lightOverrides: nLight };
}

writeFileSync(outFile, lines.join('\n') + '\n');

// ---- 4. 家族清单：生成 src/theme/families.js 供 ThemeStore 使用 ----
const manifestFile = join(frontendRoot, 'src', 'theme', 'families.js');
const manifest = `// 由 scripts/gen-themes.mjs 生成，勿手改。顺序对应 ThemeCatalog.cs。
export const themeFamilies = ${JSON.stringify(
  [
    { id: 'hatsunemiku', name: '初音未来', subtitle: '初音粉 × 初音青' },
    { id: 'deepseekpurple', name: 'DeepSeek紫', subtitle: '幻紫 × 樱粉语义' },
    { id: 'zhishublue', name: '植树蓝', subtitle: '克莱因蓝 × 晴空语义' },
    { id: 'mojangred', name: 'Mojang红', subtitle: '经典红 × 方块绿' },
  ], null, 2)};
`;
writeFileSync(manifestFile, manifest);

console.log(`themes.css: base dark=${counts.baseDark} light=${counts.baseLight}`);
for (const [id, c] of Object.entries(counts.family))
  console.log(`  ${id}: global=${c.global} darkOv=${c.darkOverrides} lightOv=${c.lightOverrides}`);
console.log(`written: ${outFile}\nwritten: ${manifestFile}`);
