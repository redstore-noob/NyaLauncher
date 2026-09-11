/*
 * PersonalizationSettingsView —— 等价 Vue 版 settings/Personalization.vue，还原
 * PersonalizationSettingsPage（外观与主题 / 工作区布局）+「实例图标」管理
 * （ContentAPI：SetCustomIcon / RemoveCustomIcon）。
 * 主题家族/明暗模式走 ThemeProvider 的 useTheme()（setFamily/setMode，热切换）。
 * PORTING_NOTES（与 Vue 版一致）：
 * - 彩虹背景/星尘特效/点击圆环：开关存 localStorage（key: nyalauncher.fx），
 *   特效渲染层由 components/overlay/FxLayer.tsx 消费。
 * - 自定义背景：SystemAPI.SelectFile 选图，经 /localfile 路由预览
 *   （localStorage key: nyalauncher.customBg；FxLayer 消费为全局壁纸）。
 * - 组件缩放：存 localStorage（key: nyalauncher.componentScale），保存后派发
 *   nya:personalization 事件。
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { SettingsCard, SettingRow, NyaToggle, NyaSlider } from '@/components/settings'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useTheme } from '../../store/theme'
import * as Config from '../../../wailsjs/go/bindings/ConfigAPI.js'
import * as Content from '../../../wailsjs/go/bindings/ContentAPI.js'
import { SelectFile } from '../../../wailsjs/go/bindings/SystemAPI.js'
import { matchAliases, useSettingsSearch } from './search'
import './settings-shared.css'

/* ---------- 搜索（Hub 调用） ---------- */
const CARD_ALIASES: Record<string, string[]> = {
  appearance: ['外观与主题', '主题', '个性化', '明暗', '跟随系统', '彩虹', '星尘', '圆环', '背景', '壁纸', '不透明度', '模糊',
    // 家族名/ID（与 Vue 版 themeStore.families.flatMap 一致）
    '初音未来', 'hatsunemiku', 'DeepSeek紫', 'deepseekpurple', '植树蓝', 'zhishublue', 'Mojang红', 'mojangred'],
  workspace: ['工作区布局', '布局', '组件尺寸', '缩放', '默认布局', '配置目录', '存储'],
  icon: ['实例图标', '自定义图标', '封面'],
}

const MODES = [
  { value: 'Dark' as const, label: '暗色' },
  { value: 'System' as const, label: '跟随系统' },
  { value: 'Light' as const, label: '浅色' },
]
// 家族预览色条（预览色固定，不随当前主题变化；对应 family.CreatePreviewBrush()）
const PREVIEW_BARS: Record<string, string> = {
  hatsunemiku: 'linear-gradient(90deg, #39c5bb, #86cecb, #e12885)',
  deepseekpurple: 'linear-gradient(90deg, #4d6bfe, #a06ee1, #f4a7b9)',
  zhishublue: 'linear-gradient(90deg, #002fa7, #3f7fbf, #7ec8e3)',
  mojangred: 'linear-gradient(90deg, #a12722, #d8734b, #6ea55e)',
}

const FX_KEY = 'nyalauncher.fx'
const BG_KEY = 'nyalauncher.customBg'
const SCALE_KEY = 'nyalauncher.componentScale'

interface BgState { path: string; dataUrl: string; opacity: number; blur: number }
interface FxState { ambient: boolean; sparkle: boolean; clickRing: boolean }

function notifyFxLayer() {
  // 特效层（components/overlay/FxLayer.tsx）即时跟随
  window.dispatchEvent(new CustomEvent('nya:personalization'))
}

export default function PersonalizationSettingsView() {
  const theme = useTheme()
  const search = useSettingsSearch()
  const query = search?.query ?? ''

  /* ---------- 卡片可见性（跟随 Hub 搜索） ---------- */
  const cardVisible = useMemo(() => {
    const q = query.trim()
    if (!q) return null
    const result: Record<string, boolean> = {}
    for (const key of Object.keys(CARD_ALIASES)) {
      result[key] = matchAliases(CARD_ALIASES[key], q)
    }
    return result
  }, [query])

  useEffect(() => {
    if (!search) return
    if (!query.trim()) { search.setCount('personalization', -1); return }
    const hits = Object.keys(CARD_ALIASES).filter((k) => matchAliases(CARD_ALIASES[k], query)).length
    search.setCount('personalization', hits)
  }, [query, search])

  /* ---------- 附加特效开关（localStorage） ---------- */
  const [fx, setFx] = useState<FxState>({ ambient: false, sparkle: false, clickRing: false })
  const fxRef = useRef(fx); fxRef.current = fx

  function saveFx(next: FxState) {
    setFx(next)
    localStorage.setItem(FX_KEY, JSON.stringify(next))
    notifyFxLayer()
  }
  function toggleFx(key: keyof FxState) {
    return (v: boolean) => saveFx({ ...fxRef.current, [key]: v })
  }

  /* ---------- 自定义背景（SelectFile 选图 + /localfile 预览） ---------- */
  const [customBg, setCustomBg] = useState<BgState>({ path: '', dataUrl: '', opacity: 0.3, blur: 0 })
  const [bgPreviewFailed, setBgPreviewFailed] = useState(false)
  const customBgRef = useRef(customBg); customBgRef.current = customBg

  const backgroundUrl = customBg.dataUrl
    || (customBg.path && !bgPreviewFailed ? `/localfile?path=${encodeURIComponent(customBg.path)}` : '')

  function saveCustomBg(next: BgState) {
    setCustomBg(next)
    localStorage.setItem(BG_KEY, JSON.stringify(next))
    notifyFxLayer()
  }
  async function pickBackground() {
    let path = ''
    try {
      path = await SelectFile('选择背景图片', '图片文件', '*.png;*.jpg;*.jpeg;*.webp;*.bmp')
    } catch { /* 用户取消 */ }
    if (!path) return
    setBgPreviewFailed(false)
    saveCustomBg({ ...customBgRef.current, path, dataUrl: '' })
  }
  function clearBackground() {
    saveCustomBg({ ...customBgRef.current, path: '', dataUrl: '' })
  }

  /* ---------- 工作区布局 ---------- */
  const [componentScale, setComponentScale] = useState(1)
  const [storageDir, setStorageDir] = useState('')
  const [manualStorageDir, setManualStorageDir] = useState('')
  const [pendingText, setPendingText] = useState('')
  const resetLayoutPending = useRef(false)
  const componentScaleRef = useRef(componentScale); componentScaleRef.current = componentScale

  function loadComponentScale() {
    const v = Number(localStorage.getItem(SCALE_KEY))
    if (!Number.isNaN(v) && v > 0) setComponentScale(Math.min(1.6, Math.max(0.65, v)))
  }
  function resetLayout() {
    resetLayoutPending.current = true
    setComponentScale(1)
    setPendingText('已标记恢复默认布局，尚未保存')
  }
  function cancelChanges() {
    resetLayoutPending.current = false
    loadComponentScale()
    setPendingText('')
  }
  async function applyStorageDir() {
    const dir = manualStorageDir.trim()
    if (dir) {
      await Config.SetStorageDirectory(dir)
      setStorageDir(dir)
    }
  }
  async function useDefaultStorageDir() {
    const def = await Config.GetDefaultStorageDirectory()
    await Config.SetStorageDirectory(def)
    setStorageDir(def)
    setManualStorageDir('')
  }
  function saveConfig() {
    localStorage.setItem(SCALE_KEY, String(componentScaleRef.current))
    notifyFxLayer()
    resetLayoutPending.current = false
    setPendingText('配置已保存')
    setTimeout(() => setPendingText((t) => (t === '配置已保存' ? '' : t)), 2000)
  }

  /* ---------- 实例图标（ContentAPI） ---------- */
  const [icon, setIcon] = useState({ instanceId: '', iconPath: '' })
  const [iconHint, setIconHint] = useState('')
  async function pickIconFile() {
    try {
      const path = await SelectFile('选择实例图标', '图片文件', '*.png')
      if (path) setIcon((s) => ({ ...s, iconPath: path }))
    } catch { /* 用户取消 */ }
  }
  async function applyIcon() {
    try {
      const dir = (await Config.GetGameDirectory()) || '.'
      await Content.SetCustomIcon(dir, icon.instanceId, icon.iconPath)
      setIconHint(`已为 ${icon.instanceId} 设置图标。`)
      setIcon((s) => ({ ...s, iconPath: '' }))
    } catch (err) {
      setIconHint(`设置失败：${(err as Error)?.message || err}`)
    }
  }
  async function removeIcon() {
    try {
      const dir = (await Config.GetGameDirectory()) || '.'
      const ok = await Content.RemoveCustomIcon(dir, icon.instanceId)
      setIconHint(ok ? `已移除 ${icon.instanceId} 的自定义图标。` : '该实例没有自定义图标。')
    } catch (err) {
      setIconHint(`移除失败：${(err as Error)?.message || err}`)
    }
  }

  /* ---------- 初始化 ---------- */
  useEffect(() => {
    try {
      const next = JSON.parse(localStorage.getItem(FX_KEY) || '{}') as Partial<FxState>
      setFx((prev) => ({ ...prev, ...next }))
    } catch { /* ignore */ }
    try {
      const bg = JSON.parse(localStorage.getItem(BG_KEY) || '{}') as Partial<BgState>
      setCustomBg({ path: '', dataUrl: '', opacity: 0.3, blur: 0, ...bg })
    } catch { /* ignore */ }
    loadComponentScale()
    Config.GetStorageDirectory().then(setStorageDir).catch(() => setStorageDir(''))
  }, [])

  const vis = (key: string) => (cardVisible === null ? true : cardVisible[key])

  return (
    <section className="page px-10 pt-10 pb-8">
      <header className="page-head">
        <h1 className="m-0 text-[28px] font-bold text-primary-text">个性化</h1>
        <p className="m-0 text-[13px] text-hint-text">主题、背景图与工作区布局</p>
      </header>

      <div className="cards">
        {/* ==================== 外观与主题 ==================== */}
        {vis('appearance') ? (
          <SettingsCard icon="🎨" title="外观与主题" subtitle="主题风格、明暗模式与自定义背景图">
            {/* 主题风格：色卡选择器（ThemeProvider 热切换） */}
            <div className="field-block" data-title="主题风格">
              <span className="text-[14px] font-semibold text-secondary-text">主题风格</span>
              <div className="flex flex-wrap gap-2">
                {theme.families.map((f) => (
                  <button
                    key={f.id}
                    className={`flex w-[148px] cursor-pointer flex-col gap-2 rounded-lg border px-3 py-2 text-left transition-colors duration-150 hover:bg-accent ${
                      theme.family === f.id ? 'border-primary bg-accent' : 'border-input bg-muted hover:border-medium-border'
                    }`}
                    onClick={() => theme.setFamily(f.id)}
                  >
                    <span className="h-[5px] rounded-full" style={{ background: PREVIEW_BARS[f.id] || 'linear-gradient(90deg, #888, #ccc)' }} />
                    <span className="flex items-center justify-between gap-1">
                      <span className="text-[13px] font-medium text-primary-text">{f.name}</span>
                      {theme.family === f.id ? (
                        <span className="flex size-4 items-center justify-center rounded-full bg-primary text-[10px] text-primary-foreground">✓</span>
                      ) : null}
                    </span>
                    <span className="text-[10px] text-muted-foreground">{f.subtitle}</span>
                  </button>
                ))}
              </div>
            </div>

            <SettingRow title="明暗模式">
              <div className="inline-flex">
                {MODES.map((m, i) => (
                  <button
                    key={m.value}
                    className={`cursor-pointer border px-4 py-2 text-[13px] transition-colors duration-150 ${
                      i === 0 ? 'rounded-l-md' : ''
                    } ${i === MODES.length - 1 ? 'rounded-r-md' : ''} ${i > 0 ? '-ml-px' : ''} ${
                      theme.mode === m.value
                        ? 'border-primary bg-primary text-primary-foreground'
                        : 'border-input bg-muted text-body-text hover:border-medium-border hover:bg-accent'
                    }`}
                    onClick={() => theme.setMode(m.value)}
                  >{m.label}</button>
                ))}
              </div>
            </SettingRow>

            {/* PORTING_NOTES: 彩虹背景 / 星尘特效 / 点击圆环由 FxLayer 渲染，开关落 localStorage */}
            <SettingRow title="彩虹背景" hint="窗口底部的主题色渐变氛围层（特效层待前端移植）">
              <NyaToggle checked={fx.ambient} onChange={toggleFx('ambient')} />
            </SettingRow>
            <SettingRow title="星尘特效" hint="鼠标移动时的星尘拖尾（特效层待前端移植）">
              <NyaToggle checked={fx.sparkle} onChange={toggleFx('sparkle')} />
            </SettingRow>
            <SettingRow title="点击圆环" hint="点击时的扩散圆环动画（特效层待前端移植）">
              <NyaToggle checked={fx.clickRing} onChange={toggleFx('clickRing')} />
            </SettingRow>

            {/* 自定义背景图 */}
            <div className="field-block rounded-lg border border-subtle-border bg-muted px-4 py-3" data-title="自定义背景 壁纸 不透明度 模糊">
              <div className="field-row">
                <div className="field-row-head">
                  <span className="text-[14px] font-semibold text-secondary-text">自定义背景图</span>
                  <div className="flex items-center gap-2">
                    <Button variant="secondary" size="sm" onClick={pickBackground}>选择图片</Button>
                    {backgroundUrl ? (
                      <Button variant="secondary" size="sm" onClick={clearBackground}>清除</Button>
                    ) : null}
                  </div>
                </div>
              </div>
              <div className="slider-line">
                <span className="text-[11px] text-muted-foreground">不透明度</span>
                <NyaSlider
                  value={customBg.opacity}
                  min={0.05}
                  max={0.85}
                  step={0.01}
                  disabled={!backgroundUrl}
                  onChange={(v) => saveCustomBg({ ...customBgRef.current, opacity: v })} />
              </div>
              <div className="slider-line">
                <span className="text-[11px] text-muted-foreground">模糊</span>
                <NyaSlider
                  value={customBg.blur}
                  min={0}
                  max={30}
                  step={1}
                  disabled={!backgroundUrl}
                  onChange={(v) => saveCustomBg({ ...customBgRef.current, blur: v })} />
              </div>
              {/* 页内预览：全局背景层由 App 壳的 FxLayer 消费 */}
              {backgroundUrl ? (
                <div className="relative h-[120px] overflow-hidden rounded-md bg-surface">
                  <img
                    src={backgroundUrl}
                    alt=""
                    onError={() => setBgPreviewFailed(true)}
                    style={{ opacity: customBg.opacity, filter: `blur(${customBg.blur}px)` }}
                    className="h-full w-full object-cover" />
                </div>
              ) : null}
            </div>
          </SettingsCard>
        ) : null}

        {/* ==================== 工作区布局 ==================== */}
        {vis('workspace') ? (
          <SettingsCard icon="▦" title="工作区布局" subtitle="组件尺寸与配置目录；修改后需点击下方「保存配置」生效">
            <SettingRow title="全局组件尺寸" hint="同时缩放组件外框、文字和点击区域">
              <NyaSlider value={componentScale} min={0.65} max={1.6} step={0.05} className="w-[260px]"
                onChange={(v) => setComponentScale(v)} />
              <span className="w-[52px] text-right font-mono text-[11px] font-semibold text-primary">
                {Math.round(componentScale * 100)}%
              </span>
            </SettingRow>

            <SettingRow title="恢复默认布局" hint="把主界面组件摆放恢复到出厂状态，点击「保存配置」后生效。">
              <Button variant="link" size="sm" onClick={resetLayout}>恢复默认</Button>
            </SettingRow>

            <div className="flex flex-col gap-2 rounded-lg border border-subtle-border bg-muted px-4 py-3" data-title="配置目录 存储">
              <div className="flex items-center gap-3">
                <span className="text-[14px] font-semibold text-secondary-text">配置目录</span>
                <span className="min-w-0 flex-1 overflow-hidden text-ellipsis whitespace-nowrap font-mono text-[10px] text-subtext-text" title={storageDir}>
                  {storageDir}
                </span>
              </div>
              <div className="dir-actions">
                <Input
                  value={manualStorageDir}
                  onChange={(e) => setManualStorageDir(e.target.value)}
                  className="min-w-[160px] flex-1"
                  placeholder="输入配置目录绝对路径" />
                <Button variant="link" size="sm" onClick={applyStorageDir}>选择目录…</Button>
                <Button variant="secondary" size="sm" onClick={useDefaultStorageDir}>平台默认</Button>
              </div>
            </div>

            <div className="save-row">
              <span className="mr-auto text-[11px] text-warning">{pendingText}</span>
              <Button variant="secondary" style={{ padding: '10px 14px' }} onClick={cancelChanges}>放弃修改</Button>
              <Button style={{ padding: '10px 18px' }} onClick={saveConfig}>保存配置</Button>
            </div>
          </SettingsCard>
        ) : null}

        {/* ==================== 实例图标 ==================== */}
        {vis('icon') ? (
          <SettingsCard icon="🖼" title="实例图标" subtitle="为实例设置自定义封面图标（覆盖默认方块图）">
            <div className="field-block rounded-lg border border-subtle-border bg-muted px-4 py-3" data-title="实例图标 自定义图标">
              <span className="text-[11px] text-hint-text">
                填写游戏目录中的实例（版本）ID 与图标文件路径；图标立即生效并在实例列表显示。
              </span>
              <div className="dir-actions">
                <Input
                  value={icon.instanceId}
                  onChange={(e) => setIcon((s) => ({ ...s, instanceId: e.target.value }))}
                  className="min-w-[160px] flex-1"
                  placeholder="实例（版本）ID，如 1.20.4-fabric" />
                <Input
                  value={icon.iconPath}
                  onChange={(e) => setIcon((s) => ({ ...s, iconPath: e.target.value }))}
                  className="min-w-[160px] flex-1"
                  placeholder="图标文件绝对路径（png/jpg）" />
                <Button variant="secondary" size="sm" onClick={pickIconFile}>选择图标…</Button>
              </div>
              <div className="dir-actions">
                <span className="text-[11px] text-hint-text">{iconHint}</span>
                <Button size="sm" disabled={!icon.instanceId || !icon.iconPath} onClick={applyIcon}>设置图标</Button>
                <Button variant="secondary" size="sm" disabled={!icon.instanceId} onClick={removeIcon}>移除图标</Button>
              </div>
            </div>
          </SettingsCard>
        ) : null}
      </div>
    </section>
  )
}
