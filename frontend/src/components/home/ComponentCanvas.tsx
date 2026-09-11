/*
 * 主内容区自由摆放画布（ComponentCanvas.axaml + .axaml.cs 的 React 平移，参照
 * Vue 版 ComponentCanvas.vue 逐功能对照）：
 * - 组件按归一化坐标（0~1）绝对定位，落位 16px 网格吸附；
 * - 标题手柄拖动移动（拖动中显示底部垃圾桶，拖入即删，confirm 后生效）；
 * - 编辑模式：网格线 / 吸附参考线 / 缩放·旋转手柄 / 删除按钮；
 * - 左下角「组件库」按钮打开 400px 抽屉，点击或拖入添加组件。
 * 拖拽用 pointer events + window 监听（等价 Vue 版）。
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { confirm } from '@/components/overlay/dialog'
import { GetValue, SetValue } from '../../../wailsjs/go/bindings/ConfigAPI.js'
import {
  COMPONENT_DEFINITIONS, DRAG_START_THRESHOLD, MAX_SIZE_SCALE, MIN_SIZE_SCALE,
  ROTATION_SNAP_DEGREES, SNAP_GRID, type ComponentDefinition,
} from './componentRegistry'
import CanvasComponent from './CanvasComponent'
import ComponentLibraryDrawer from './ComponentLibraryDrawer'
import { useHomeEditMode } from './homeEditMode'
import './ComponentCanvas.css'

interface Shell {
  id: string
  def: ComponentDefinition
  relX: number
  relY: number
  z: number
  sizeScale: number
  rotation: number
  removing: boolean
}

interface Placement {
  componentId?: string
  relativeX?: number
  relativeY?: number
  zIndex?: number
  sizeScale?: number
  rotationDegrees?: number
}

interface DragState {
  mode: 'move' | 'resize' | 'rotate'
  shell: Shell
  grabX: number
  grabY: number
  moved: boolean
  startX: number
  startY: number
  startScale: number
  liveX: number
  liveY: number
}

interface LibDragState {
  def: ComponentDefinition
  moved: boolean
  startX: number
  startY: number
  x: number
  y: number
}

// ---- 全局组件缩放（PersonalizationSettingsPage 的组件尺寸滑条）----
const COMPONENT_SCALE_KEY = 'nyalauncher.componentScale'
function readGlobalScale(): number {
  const v = Number(localStorage.getItem(COMPONENT_SCALE_KEY))
  return Number.isFinite(v) && v > 0 ? Math.min(1.6, Math.max(0.65, v)) : 1
}

function sanitizeSizeScale(v: unknown): number {
  const n = Number(v)
  return n > 0.05 && n <= 4 ? n : 1 // 旧档案缺失字段兜底（SanitizeSizeScale）
}

// 摆放持久化：ConfigAPI.SetValue / GetValue（Go 移植版先落 config.json 单键 JSON）
const SAVE_KEY = 'workspaceComponentPlacements'

export default function ComponentCanvas() {
  // 编辑模式：App.vue 状态栏按钮的 editMode（provide/inject 等价 → Context）
  const { editing } = useHomeEditMode()

  const canvasRef = useRef<HTMLDivElement | null>(null)
  const [canvasSize, setCanvasSize] = useState({ width: 0, height: 0 })
  const canvasSizeRef = useRef(canvasSize)
  canvasSizeRef.current = canvasSize

  const [shells, setShells] = useState<Shell[]>([])
  const shellsRef = useRef<Shell[]>([])
  shellsRef.current = shells
  const maxZRef = useRef(0)
  const loadedRef = useRef(false)
  const saveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const [globalScale, setGlobalScale] = useState(1)
  const globalScaleRef = useRef(1)

  const [dragRender, setDragRender] = useState<{ moved: boolean; liveX: number; liveY: number } | null>(null)
  const dragRef = useRef<DragState | null>(null)
  const [deleteHot, setDeleteHot] = useState(false)

  const [libraryOpen, setLibraryOpen] = useState(false)
  const [libDrag, setLibDrag] = useState<LibDragState | null>(null)
  const libDragRef = useRef<LibDragState | null>(null)
  libDragRef.current = libDrag

  const placedIds = useMemo(() => new Set(shells.map((s) => s.id)), [shells])
  const placedIdsRef = useRef(placedIds)
  placedIdsRef.current = placedIds

  const deleteZoneVisible = dragRef.current != null && dragRender != null

  // ---- 几何：归一化坐标 → 吸附像素（PositionShell 语义）----
  const shellPx = useCallback((s: Pick<Shell, 'def' | 'relX' | 'relY' | 'sizeScale'>) => {
    const scale = globalScaleRef.current
    const w = s.def.preferredWidth * s.sizeScale * scale
    const h = s.def.preferredHeight * s.sizeScale * scale
    const { width, height } = canvasSizeRef.current
    const availW = Math.max(0, width - w)
    const availH = Math.max(0, height - h)
    let x = Math.round((Math.min(1, Math.max(0, s.relX)) * availW) / SNAP_GRID) * SNAP_GRID
    let y = Math.round((Math.min(1, Math.max(0, s.relY)) * availH) / SNAP_GRID) * SNAP_GRID
    x = Math.min(Math.max(0, x), Math.max(0, availW))
    y = Math.min(Math.max(0, y), Math.max(0, availH))
    return { x, y, w, h }
  }, [])

  function shellStyle(s: Shell): React.CSSProperties {
    const { x, y, w, h } = shellPx(s)
    return {
      left: `${x}px`,
      top: `${y}px`,
      width: `${w}px`,
      height: `${h}px`,
      zIndex: s.z,
      transform: `rotate(${s.rotation}deg)`,
      transformOrigin: 'center',
      opacity: s.removing ? 0 : undefined,
      scale: s.removing ? '0.2' : undefined,
      transition: s.removing
        ? 'opacity 240ms var(--ease-emphasized-accelerate), scale 240ms var(--ease-emphasized-accelerate)'
        : undefined,
    }
  }

  // ---- 持久化（ScheduleWorkspaceProfileSave 的 500ms 防抖语义）----
  const serializePlacements = useCallback((list: Shell[]): Placement[] => list.map((s) => ({
    componentId: s.id,
    relativeX: s.relX,
    relativeY: s.relY,
    zIndex: s.z,
    sizeScale: s.sizeScale,
    rotationDegrees: s.rotation,
  })), [])

  const persistNow = useCallback(async (list?: Shell[]) => {
    if (saveTimerRef.current) { clearTimeout(saveTimerRef.current); saveTimerRef.current = null }
    try {
      await SetValue(SAVE_KEY, JSON.stringify(serializePlacements(list ?? shellsRef.current)))
    } catch (e) {
      console.error('[canvas] 保存摆放失败', e)
    }
  }, [serializePlacements])

  const schedulePersist = useCallback(() => {
    if (saveTimerRef.current) clearTimeout(saveTimerRef.current)
    saveTimerRef.current = setTimeout(() => { persistNow() }, 500)
  }, [persistNow])

  // 摆放变化 → 防抖保存（跳过初始加载；卸载时由 cleanup 立即落盘）
  useEffect(() => {
    if (!loadedRef.current) return
    schedulePersist()
    return () => { if (saveTimerRef.current) clearTimeout(saveTimerRef.current) }
  }, [shells, schedulePersist])

  const updateShell = useCallback((id: string, patch: Partial<Shell>) => {
    setShells((prev) => prev.map((s) => (s.id === id ? { ...s, ...patch } : s)))
  }, [])

  // ---- 添加 / 删除 ----
  const addShell = useCallback((def: ComponentDefinition, relX = 0.32, relY = 0.3, z: number | null = null, sizeScale = 1, rotation = 0): Shell | null => {
    if (shellsRef.current.some((s) => s.id === def.id)) return null
    const shell: Shell = {
      id: def.id, def, relX, relY,
      z: z ?? ++maxZRef.current, sizeScale: sanitizeSizeScale(sizeScale), rotation,
      removing: false,
    }
    setShells((prev) => [...prev, shell])
    schedulePersist()
    return shell
  }, [schedulePersist])

  const removeShell = useCallback(async (s: Shell) => {
    updateShell(s.id, { removing: true }) // 退场：缩小淡出（240ms accelerate，原版 M3 曲线）
    await new Promise((r) => setTimeout(r, 240))
    setShells((prev) => prev.filter((x) => x.id !== s.id))
    schedulePersist()
  }, [updateShell, schedulePersist])

  const removeWithConfirm = useCallback(async (s: Shell) => {
    if (await confirm('删除组件', `确定要从画布移除「${s.def.title}」吗？`)) {
      await removeShell(s)
    }
  }, [removeShell])

  const raiseShell = useCallback((s: Shell) => {
    updateShell(s.id, { z: ++maxZRef.current })
    schedulePersist()
  }, [updateShell, schedulePersist])

  // ---- 指针拖拽（move / resize / rotate；指针捕获语义用 window 监听等价实现）----
  const canvasPoint = useCallback((e: PointerEvent | React.PointerEvent) => {
    const r = canvasRef.current!.getBoundingClientRect()
    return { x: e.clientX - r.left, y: e.clientY - r.top }
  }, [])

  function onDeleteZonePoint(p: { x: number; y: number }): boolean {
    // 垃圾桶圆心 ≈ (canvas.width/2, canvas.height - 88)，半径 30 + 6 容差（原版同款）
    const { width, height } = canvasSizeRef.current
    const cx = width / 2
    const cy = height - 88
    const dx = p.x - cx
    const dy = p.y - cy
    return dx * dx + dy * dy <= 36 * 36
  }

  const beginDrag = useCallback((payload: DragState) => {
    dragRef.current = payload
    setDragRender({ moved: false, liveX: payload.liveX, liveY: payload.liveY })
    window.addEventListener('pointermove', onPointerMove)
    window.addEventListener('pointerup', onPointerUp, { once: true })
    window.addEventListener('pointercancel', onPointerUp, { once: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const startMove = useCallback((s: Shell, e: React.PointerEvent) => {
    if (e.button !== 0) return
    e.stopPropagation()
    raiseShell(s)
    const p = canvasPoint(e)
    const { x, y } = shellPx(s)
    beginDrag({ mode: 'move', shell: s, grabX: p.x - x, grabY: p.y - y, moved: false, startX: e.clientX, startY: e.clientY, startScale: s.sizeScale, liveX: x, liveY: y })
  }, [raiseShell, canvasPoint, shellPx, beginDrag])

  const startResize = useCallback((s: Shell, e: React.PointerEvent) => {
    if (e.button !== 0) return
    e.stopPropagation()
    raiseShell(s)
    const p = canvasPoint(e)
    beginDrag({ mode: 'resize', shell: s, grabX: 0, grabY: 0, moved: false, startX: p.x, startY: p.y, startScale: s.sizeScale, liveX: 0, liveY: 0 })
  }, [raiseShell, canvasPoint, beginDrag])

  const startRotate = useCallback((s: Shell, e: React.PointerEvent) => {
    if (e.button !== 0) return
    e.stopPropagation()
    raiseShell(s)
    const p = canvasPoint(e)
    beginDrag({ mode: 'rotate', shell: s, grabX: 0, grabY: 0, moved: false, startX: p.x, startY: p.y, startScale: s.sizeScale, liveX: 0, liveY: 0 })
  }, [raiseShell, canvasPoint, beginDrag])

  const onPointerMove = useCallback((e: PointerEvent) => {
    const d = dragRef.current
    if (!d) return
    const p = canvasPoint(e)
    const s = d.shell

    if (d.mode === 'move') {
      if (!d.moved && Math.hypot(e.clientX - d.startX, e.clientY - d.startY) < DRAG_START_THRESHOLD) return
      d.moved = true
      const { w, h } = shellPx(s)
      const { width, height } = canvasSizeRef.current
      const availW = Math.max(0, width - w)
      const availH = Math.max(0, height - h)
      const x = Math.round(Math.min(Math.max(0, p.x - d.grabX), availW) / SNAP_GRID) * SNAP_GRID
      const y = Math.round(Math.min(Math.max(0, p.y - d.grabY), availH) / SNAP_GRID) * SNAP_GRID
      // 归一化坐标同步更新，落位即最终值（MoveShellLive 语义）
      updateShell(s.id, {
        relX: availW <= 0 ? 0 : Math.min(1, Math.max(0, x / availW)),
        relY: availH <= 0 ? 0 : Math.min(1, Math.max(0, y / availH)),
      })
      d.liveX = x
      d.liveY = y
      setDragRender({ moved: true, liveX: x, liveY: y })
      setDeleteHot(onDeleteZonePoint(p))
    } else if (d.mode === 'resize') {
      // 取水平 / 垂直位移中较大的一个作为等比缩放驱动量（原版 AttachResizeGrip 语义）
      const delta = Math.max(p.x - d.startX, p.y - d.startY)
      const startW = s.def.preferredWidth * d.startScale
      const newSize = Math.min(
        Math.max(startW + delta, s.def.preferredWidth * MIN_SIZE_SCALE),
        s.def.preferredWidth * MAX_SIZE_SCALE,
      )
      updateShell(s.id, { sizeScale: newSize / s.def.preferredWidth })
    } else if (d.mode === 'rotate') {
      // 指针相对组件中心的角度即旋转角，15° 吸附（原版 AttachRotateGrip 语义）
      const { x, y, w, h } = shellPx(s)
      const angle = (Math.atan2(p.y - (y + h / 2), p.x - (x + w / 2)) * 180) / Math.PI + 90
      let snapped = Math.round(angle / ROTATION_SNAP_DEGREES) * ROTATION_SNAP_DEGREES
      snapped = snapped >= 180 ? snapped - 360 : snapped <= -180 ? snapped + 360 : snapped
      updateShell(s.id, { rotation: snapped })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canvasPoint, shellPx, updateShell])

  const onPointerUp = useCallback(async (e: PointerEvent) => {
    window.removeEventListener('pointermove', onPointerMove)
    const d = dragRef.current
    dragRef.current = null
    setDragRender(null)
    setDeleteHot(false)
    if (!d || d.mode !== 'move' || !d.moved) return
    const p = canvasPoint(e)

    // 拖到底部垃圾桶 = 删除（先判命中再隐藏，原版 EndMoveDrag 注释强调的顺序）
    if (onDeleteZonePoint(p)) {
      if (await confirm('删除组件', `确定要删除「${d.shell.def.title}」吗？`)) {
        const current = shellsRef.current.find((s) => s.id === d.shell.id)
        if (current) await removeShell(current)
      }
      return
    }
    schedulePersist()
    // 落位回弹：原版 BounceAsync(root, 1.03, 180) 由 shell settle 动画近似
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [onPointerMove, canvasPoint, removeShell, schedulePersist])

  // ---- 组件库：点击添加 / 按下即拖（ComponentLibraryView 拖出语义）----
  const onLibPointerMove = useCallback((e: PointerEvent) => {
    const d = libDragRef.current
    if (!d) return
    setLibDrag({ ...d, x: e.clientX, y: e.clientY })
    if (!d.moved && Math.hypot(e.clientX - d.startX, e.clientY - d.startY) >= DRAG_START_THRESHOLD) {
      d.moved = true
      setLibraryOpen(false) // 原版语义：拖拽启动即收起抽屉
    }
  }, [])

  const onLibPointerUp = useCallback(async (e: PointerEvent) => {
    window.removeEventListener('pointermove', onLibPointerMove)
    const d = libDragRef.current
    setLibDrag(null)
    if (!d) return
    const canvasRect = canvasRef.current?.getBoundingClientRect()
    if (!canvasRect) return
    const overCanvas = e.clientX >= canvasRect.left && e.clientX <= canvasRect.right
      && e.clientY >= canvasRect.top && e.clientY <= canvasRect.bottom
    if (!overCanvas) return

    if (d.moved) {
      // 拖入：落点即组件中心（原版 GetRelativeCentered 语义）
      const w = d.def.preferredWidth
      const h = d.def.preferredHeight
      const availW = Math.max(1, canvasRect.width - w)
      const availH = Math.max(1, canvasRect.height - h)
      const relX = Math.min(1, Math.max(0, (e.clientX - canvasRect.left - w / 2) / availW))
      const relY = Math.min(1, Math.max(0, (e.clientY - canvasRect.top - h / 2) / availH))
      addShell(d.def, relX, relY)
    } else {
      // 点击：默认位置（原版 AddRequested → AddComponent 默认 0.32/0.30）
      addShell(d.def)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [onLibPointerMove, addShell])

  const startLibraryDrag = useCallback((def: ComponentDefinition, e: React.PointerEvent) => {
    if (e.button !== 0 || placedIdsRef.current.has(def.id)) return
    const d: LibDragState = { def, moved: false, startX: e.clientX, startY: e.clientY, x: e.clientX, y: e.clientY }
    setLibDrag(d)
    window.addEventListener('pointermove', onLibPointerMove)
    window.addEventListener('pointerup', onLibPointerUp, { once: true })
    window.addEventListener('pointercancel', onLibPointerUp, { once: true })
  }, [onLibPointerMove, onLibPointerUp])

  /** 导入摆放记录（对应 LoadPlacements）：未覆盖的注册组件按三列瀑布流补默认位置。 */
  const loadPlacements = useCallback((list: Placement[] | null | undefined) => {
    const known = new Set<string>()
    const added: Shell[] = []
    for (const p of list ?? []) {
      const def = COMPONENT_DEFINITIONS.find((d) => d.id.toLowerCase() === String(p.componentId ?? '').toLowerCase())
      if (!def || known.has(def.id)) continue
      known.add(def.id)
      const shell: Shell = {
        id: def.id, def,
        relX: p.relativeX ?? 0, relY: p.relativeY ?? 0,
        z: p.zIndex ?? ++maxZRef.current,
        sizeScale: sanitizeSizeScale(p.sizeScale),
        rotation: p.rotationDegrees ?? 0,
        removing: false,
      }
      added.push(shell)
    }
    let next = 0
    for (const def of COMPONENT_DEFINITIONS) {
      if (known.has(def.id)) continue
      added.push({
        id: def.id, def,
        relX: 0.03 + (next % 3) * 0.33, relY: 0.05 + Math.floor(next / 3) * 0.3,
        z: ++maxZRef.current, sizeScale: 1, rotation: 0, removing: false,
      })
      next++
    }
    setShells(added)
  }, [])

  // 进入编辑模式自动打开组件库抽屉（原版 EnterEditMode 语义）
  useEffect(() => {
    if (editing) setLibraryOpen(true)
  }, [editing])

  const reloadGlobalScale = useCallback(() => {
    const v = readGlobalScale()
    globalScaleRef.current = v
    setGlobalScale(v)
  }, [])

  // 初始化：全局缩放 / 尺寸监听 / 恢复摆放（等价 Vue onMounted，useEffect 清理）
  useEffect(() => {
    reloadGlobalScale()
    window.addEventListener('nya:personalization', reloadGlobalScale)
    window.addEventListener('storage', reloadGlobalScale)
    const observer = new ResizeObserver((entries) => {
      const r = entries[0].contentRect
      setCanvasSize({ width: r.width, height: r.height })
    })
    if (canvasRef.current) observer.observe(canvasRef.current)

    // 恢复摆放（GetValue 无值时返回空串 → 走瀑布流默认位置）
    ;(async () => {
      let savedPlacements: Placement[] = []
      try {
        const raw = await GetValue(SAVE_KEY)
        if (raw) savedPlacements = JSON.parse(raw)
      } catch (e) {
        console.error('[canvas] 读取摆放失败（开发模式无后端时属预期）', e)
      }
      loadPlacements(savedPlacements)
      loadedRef.current = true
    })()

    return () => {
      observer.disconnect()
      window.removeEventListener('nya:personalization', reloadGlobalScale)
      window.removeEventListener('storage', reloadGlobalScale)
      window.removeEventListener('pointermove', onPointerMove)
      window.removeEventListener('pointermove', onLibPointerMove)
      persistNow() // 卸载前落盘（等价 Vue onBeforeUnmount 的 persistNow）
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const libPreviewStyle: React.CSSProperties | undefined = libDrag?.moved ? {
    left: `${libDrag.x - libDrag.def.preferredWidth * globalScale / 2}px`,
    top: `${libDrag.y - libDrag.def.preferredHeight * globalScale / 2}px`,
    width: `${libDrag.def.preferredWidth * globalScale}px`,
    height: `${libDrag.def.preferredHeight * globalScale}px`,
  } : undefined

  return (
    <div className="relative h-full min-h-0 overflow-hidden">
      {/* 画布桌面 */}
      <div ref={canvasRef} className="absolute inset-0">
        {/* 编辑模式网格（16px 吸附网格可视化） */}
        {editing ? <div className="pointer-events-none absolute inset-0 canvas-grid" /> : null}

        {/* 组件外壳 */}
        {shells.map((s) => (
          <div
            key={s.id}
            className={`shell absolute flex select-none flex-col rounded-[14px] border bg-card ${
              s.def.isPrimary
                ? 'border-accent-bright bg-accent-deep text-primary-foreground'
                : 'border-card-border'
            } ${dragRef.current && dragRender && dragRef.current.shell.id === s.id ? 'shadow-lg' : 'shadow-sm'}`}
            style={shellStyle(s)}
            onPointerDown={() => raiseShell(s)}
          >
            {/* 标题手柄 = 移动把手（整个标题行命中；内容区交互不受影响） */}
            <div
              className="flex cursor-move touch-none items-center gap-1.5 px-2.5 pb-0.5 pt-1.5"
              onPointerDown={(e) => startMove(s, e)}
            >
              <span className={`grid h-4.5 w-4.5 shrink-0 place-items-center rounded-md text-[10px] ${
                s.def.isPrimary ? 'bg-primary-foreground/20' : 'bg-muted'
              }`}>{s.def.glyph}</span>
              <span className={`min-w-0 flex-1 truncate text-[11px] font-semibold ${
                s.def.isPrimary ? 'text-primary-foreground' : 'text-primary'
              }`}>{s.def.title}</span>
              <span className="text-[11px] leading-none opacity-60">⠿</span>
            </div>

            {/* 内容区 */}
            <div className="min-h-0 flex-1 px-2 pb-2">
              <CanvasComponent id={s.id} title={s.def.title} />
            </div>

            {/* 编辑模式手柄：右上角旋转 / 右下角缩放 / 删除按钮（仅编辑模式） */}
            {editing ? (
              <>
                <div
                  className="absolute -right-px -top-px grid h-4.5 w-4.5 cursor-grab touch-none place-items-center rounded-bl-[10px] rounded-tr-[14px] bg-primary text-[10px] text-primary-foreground opacity-90"
                  title="拖动旋转组件（15° 吸附）"
                  onPointerDown={(e) => startRotate(s, e)}
                >↻</div>
                <div
                  className="absolute -bottom-px -right-px grid h-4.5 w-4.5 cursor-nwse-resize touch-none place-items-center rounded-br-[14px] rounded-tl-[10px] bg-primary text-[9px] text-primary-foreground opacity-90"
                  title="拖动调整组件大小"
                  onPointerDown={(e) => startResize(s, e)}
                >◢</div>
                <button
                  className="absolute -left-1.5 -top-1.5 z-10 grid size-5 cursor-pointer place-items-center rounded-full border border-destructive bg-destructive text-[10px] font-bold text-primary-foreground shadow"
                  title="删除组件"
                  onPointerDown={(e) => e.stopPropagation()}
                  onClick={() => removeWithConfirm(s)}
                >✕</button>
              </>
            ) : null}
          </div>
        ))}

        {/* 拖拽吸附参考线（拖动中显示组件左缘 / 上缘位置） */}
        {dragRef.current && dragRender && dragRef.current.mode === 'move' && dragRender.moved ? (
          <>
            <div className="pointer-events-none absolute h-full w-px bg-primary/70" style={{ left: `${dragRender.liveX}px` }} />
            <div className="pointer-events-none absolute h-px w-full bg-primary/70" style={{ top: `${dragRender.liveY}px` }} />
          </>
        ) : null}
      </div>

      {/* 空画布提示 */}
      {shells.length === 0 ? (
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center gap-2">
          <span className="text-[13px] text-hint-text">画布还是空的</span>
          <span className="text-[11px] text-hint-text">打开左下角「组件库」，把组件拖进来或点击添加吧~</span>
        </div>
      ) : null}

      {/* 拖拽垃圾桶（拖动组件 / 组件库拖入时出现；preview3 同款 60x60 红圈） */}
      {deleteZoneVisible ? (
        <div className="pointer-events-none absolute bottom-14 left-1/2 z-40 flex -translate-x-1/2 flex-col items-center">
          <div
            className={`grid size-[60px] place-items-center rounded-full border-[1.5px] border-destructive transition-all duration-150 ${
              deleteHot ? 'scale-110 bg-destructive/30' : 'bg-destructive/15'
            }`}
          >
            <span className="text-[26px] leading-none">🗑</span>
          </div>
          <span className="mt-1.5 text-[12px] font-semibold text-destructive">
            {deleteHot ? '松手删除组件' : '松手删除'}
          </span>
        </div>
      ) : null}

      {/* 组件库抽屉（ComponentLibraryDrawerWidth = 400） */}
      {libraryOpen ? (
        <ComponentLibraryDrawer
          definitions={COMPONENT_DEFINITIONS}
          placedIds={placedIds}
          onClose={() => setLibraryOpen(false)}
          onItemPointerDown={startLibraryDrag}
        />
      ) : null}

      {/* 组件库拖入预览（半透明占位卡，中心跟随指针） */}
      {libDrag?.moved ? (
        <div
          className="pointer-events-none fixed z-[100] flex flex-col gap-1 rounded-[14px] border-2 border-primary bg-card p-3.5 opacity-75"
          style={libPreviewStyle}
        >
          <span className="text-[15px]">{libDrag.def.glyph}</span>
          <span className="text-[13px] font-semibold text-primary">{libDrag.def.title}</span>
          <span className="text-[10px] text-hint-text">释放后放置于此</span>
        </div>
      ) : null}

      {/* 组件库入口按钮（左下角） */}
      <Button
        variant="outline"
        size="sm"
        className="add-component-button absolute bottom-3 left-3 z-30 h-7 gap-1.5 px-2.5 text-[11px]"
        onClick={() => setLibraryOpen((v) => !v)}
      >
        <span>⊞</span> 组件库
      </Button>
    </div>
  )
}
