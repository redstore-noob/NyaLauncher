<template>
  <!--
    主内容区自由摆放画布（ComponentCanvas.axaml + .axaml.cs 移植）：
    - 组件按归一化坐标（0~1）绝对定位，落位 16px 网格吸附；
    - 标题手柄拖动移动（拖动中显示底部垃圾桶，拖入即删，confirm 后生效）；
    - 编辑模式：网格线 / 吸附参考线 / 缩放·旋转手柄 / 删除按钮；
    - 左下角「组件库」按钮打开 400px 抽屉，点击或拖入添加组件。
  -->
  <div ref="rootEl" class="relative h-full min-h-0 overflow-hidden">
    <!-- 画布桌面 -->
    <div ref="canvasEl" class="absolute inset-0">
      <!-- 编辑模式网格（16px 吸附网格可视化） -->
      <div v-if="editing" class="pointer-events-none absolute inset-0 canvas-grid" />

      <!-- 组件外壳 -->
      <div
        v-for="s in shells"
        :key="s.id"
        class="shell absolute flex select-none flex-col rounded-[14px] border bg-card"
        :class="[
          s.def.isPrimary ? 'border-accent-bright bg-accent-deep text-primary-foreground' : 'border-card-border',
          drag && drag.shellId === s.id ? 'shadow-lg' : 'shadow-sm',
        ]"
        :style="shellStyle(s)"
        @pointerdown="raiseShell(s)"
      >
        <!-- 标题手柄 = 移动把手（整个标题行命中；内容区交互不受影响） -->
        <div
          class="flex cursor-move touch-none items-center gap-1.5 px-2.5 pb-0.5 pt-1.5"
          @pointerdown="startMove(s, $event)"
        >
          <span class="grid h-4.5 w-4.5 shrink-0 place-items-center rounded-md text-[10px]"
            :class="s.def.isPrimary ? 'bg-primary-foreground/20' : 'bg-muted'">{{ s.def.glyph }}</span>
          <span class="min-w-0 flex-1 truncate text-[11px] font-semibold"
            :class="s.def.isPrimary ? 'text-primary-foreground' : 'text-primary'">{{ s.def.title }}</span>
          <span class="text-[11px] leading-none opacity-60">⠿</span>
        </div>

        <!-- 内容区 -->
        <div class="min-h-0 flex-1 px-2 pb-2">
          <CanvasComponentView :id="s.id" :title="s.def.title" />
        </div>

        <!-- 编辑模式手柄：右上角旋转 / 右下角缩放 / 删除按钮（仅编辑模式） -->
        <template v-if="editing">
          <div
            class="absolute -right-px -top-px grid h-4.5 w-4.5 cursor-grab touch-none place-items-center rounded-bl-[10px] rounded-tr-[14px] bg-primary text-[10px] text-primary-foreground opacity-90"
            title="拖动旋转组件（15° 吸附）"
            @pointerdown="startRotate(s, $event)"
          >↻</div>
          <div
            class="absolute -bottom-px -right-px grid h-4.5 w-4.5 cursor-nwse-resize touch-none place-items-center rounded-br-[14px] rounded-tl-[10px] bg-primary text-[9px] text-primary-foreground opacity-90"
            title="拖动调整组件大小"
            @pointerdown="startResize(s, $event)"
          >◢</div>
          <button
            class="absolute -left-1.5 -top-1.5 grid size-5 cursor-pointer place-items-center rounded-full border border-destructive bg-destructive text-[10px] font-bold text-primary-foreground shadow"
            title="删除组件"
            @pointerdown.stop
            @click="removeWithConfirm(s)"
          >✕</button>
        </template>
      </div>

      <!-- 拖拽吸附参考线（拖动中显示组件左缘 / 上缘位置） -->
      <template v-if="drag && drag.mode === 'move' && drag.moved">
        <div class="pointer-events-none absolute h-full w-px bg-primary/70" :style="guideVStyle" />
        <div class="pointer-events-none absolute h-px w-full bg-primary/70" :style="guideHStyle" />
      </template>
    </div>

    <!-- 空画布提示 -->
    <div v-if="shells.length === 0" class="pointer-events-none absolute inset-0 flex flex-col items-center justify-center gap-2">
      <span class="text-[13px] text-hint-text">画布还是空的</span>
      <span class="text-[11px] text-hint-text">打开左下角「组件库」，把组件拖进来或点击添加吧~</span>
    </div>

    <!-- 拖拽垃圾桶（拖动组件 / 组件库拖入时出现；preview3 同款 60x60 红圈） -->
    <div
      v-if="deleteZoneVisible"
      class="pointer-events-none absolute bottom-14 left-1/2 z-40 flex -translate-x-1/2 flex-col items-center"
    >
      <div
        class="grid size-[60px] place-items-center rounded-full border-[1.5px] border-destructive transition-all duration-150"
        :class="deleteHot ? 'scale-110 bg-destructive/30' : 'bg-destructive/15'"
      >
        <span class="text-[26px] leading-none">🗑</span>
      </div>
      <span class="mt-1.5 text-[12px] font-semibold text-destructive">
        {{ deleteHot ? '松手删除组件' : '松手删除' }}
      </span>
    </div>

    <!-- 组件库抽屉（ComponentLibraryDrawerWidth = 400） -->
    <div v-if="libraryOpen" class="absolute inset-0 z-50" @pointerdown="closeLibrary">
      <div class="absolute inset-0 bg-overlay" />
      <aside
        class="drawer-in absolute bottom-0 left-0 top-0 flex w-[400px] flex-col border-r border-border bg-popover shadow-xl"
        @pointerdown.stop
      >
        <div class="flex items-center gap-2 border-b border-subtle-border px-4 py-3">
          <span class="text-[15px] font-semibold text-foreground">组件库</span>
          <span class="text-[10px] text-hint-text">点击或拖到画布添加</span>
          <Button variant="ghost" size="sm" class="ml-auto h-7 w-7 p-0" @click="closeLibrary">✕</Button>
        </div>
        <div class="flex-1 space-y-1.5 overflow-y-auto p-3">
          <button
            v-for="(d, i) in definitions"
            :key="d.id"
            class="btn-anim flex h-[72px] w-full items-center gap-3 rounded-[14px] border p-3 text-left"
            :class="d.isPrimary
              ? 'border-accent-bright bg-accent-deep text-primary-foreground'
              : 'border-card-border bg-card text-foreground hover:bg-accent'"
            :disabled="placedIds.has(d.id)"
            :style="{ animation: 'nya-pop-in 220ms var(--ease-emphasized-decelerate) both', animationDelay: `${i * 40}ms` }"
            @pointerdown="startLibraryDrag(d, $event)"
          >
            <span class="grid size-9 shrink-0 place-items-center rounded-lg text-[15px]"
              :class="d.isPrimary ? 'bg-primary-foreground/20' : 'bg-muted'">{{ d.glyph }}</span>
            <span class="min-w-0 flex-1">
              <span class="block truncate text-[13px] font-semibold">{{ d.title }}</span>
              <span class="block truncate text-[10px] text-muted-foreground">{{ d.description }}</span>
            </span>
            <span class="shrink-0 text-[11px] text-muted-foreground">
              {{ placedIds.has(d.id) ? '已添加' : '⠿' }}
            </span>
          </button>
        </div>
      </aside>
    </div>

    <!-- 组件库拖入预览（半透明占位卡，中心跟随指针） -->
    <div
      v-if="libDrag && libDrag.moved"
      class="pointer-events-none fixed z-[100] flex flex-col gap-1 rounded-[14px] border-2 border-primary bg-card p-3.5 opacity-75"
      :style="libPreviewStyle"
    >
      <span class="text-[15px]">{{ libDrag.def.glyph }}</span>
      <span class="text-[13px] font-semibold text-primary">{{ libDrag.def.title }}</span>
      <span class="text-[10px] text-hint-text">释放后放置于此</span>
    </div>

    <!-- 组件库入口按钮（左下角） -->
    <Button
      variant="outline"
      size="sm"
      class="add-component-button absolute bottom-3 left-3 z-30 h-7 gap-1.5 px-2.5 text-[11px]"
      @click="libraryOpen = !libraryOpen"
    >
      <span>⊞</span> 组件库
    </Button>
  </div>
</template>

<script setup>
import { computed, inject, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { Button } from '@/components/ui/button'
import { confirm } from '@/composables/dialog'
import { GetValue, SetValue } from '../../../../wailsjs/go/bindings/ConfigAPI'
import {
  COMPONENT_DEFINITIONS, DRAG_START_THRESHOLD, MAX_SIZE_SCALE, MIN_SIZE_SCALE,
  ROTATION_SNAP_DEGREES, SNAP_GRID,
} from './componentRegistry'
import CanvasComponentView from './CanvasComponentView.vue'

// 编辑模式：App.vue 状态栏按钮的 editMode ref（provide/inject 接入，App.vue 逻辑零改动）
const editing = inject('homeEditMode', ref(false))

// ---- 画布状态 ----
const canvasEl = ref(null)
const canvasSize = reactive({ width: 0, height: 0 })
let resizeObserver = null

/** shells：画布上的组件摆放（对应 ComponentCanvas.axaml.cs 的 Shell）。 */
const shells = ref([])
let maxZ = 0

const definitions = COMPONENT_DEFINITIONS
const placedIds = computed(() => new Set(shells.value.map((s) => s.id)))

// ---- 全局组件缩放（PersonalizationSettingsPage 的组件尺寸滑条）：
//      localStorage nyalauncher.componentScale，保存后经 nya:personalization /
//      storage 事件即时跟随（FeatureAreaRegistry 未移植的前端等价消费层） ----
const COMPONENT_SCALE_KEY = 'nyalauncher.componentScale'
const globalScale = ref(1)
function reloadGlobalScale() {
  const v = Number(localStorage.getItem(COMPONENT_SCALE_KEY))
  globalScale.value = Number.isFinite(v) && v > 0 ? Math.min(1.6, Math.max(0.65, v)) : 1
}

// ---- 几何：归一化坐标 → 吸附像素（PositionShell 语义） ----
function shellPx(s) {
  const w = s.def.preferredWidth * s.sizeScale * globalScale.value
  const h = s.def.preferredHeight * s.sizeScale * globalScale.value
  const availW = Math.max(0, canvasSize.width - w)
  const availH = Math.max(0, canvasSize.height - h)
  let x = Math.round((Math.min(1, Math.max(0, s.relX)) * availW) / SNAP_GRID) * SNAP_GRID
  let y = Math.round((Math.min(1, Math.max(0, s.relY)) * availH) / SNAP_GRID) * SNAP_GRID
  x = Math.min(Math.max(0, x), Math.max(0, availW))
  y = Math.min(Math.max(0, y), Math.max(0, availH))
  return { x, y, w, h }
}

function shellStyle(s) {
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

// ---- 摆放持久化：ConfigAPI.SetValue / GetValue（internal/config 的任意配置项读写；
//      Go 移植版暂无 workspace.json 专用 API，先落 config.json 单键 JSON） ----
const SAVE_KEY = 'workspaceComponentPlacements'
let saveTimer = null

function serializePlacements() {
  return shells.value.map((s) => ({
    componentId: s.id,
    relativeX: s.relX,
    relativeY: s.relY,
    zIndex: s.z,
    sizeScale: s.sizeScale,
    rotationDegrees: s.rotation,
  }))
}

function schedulePersist() {
  clearTimeout(saveTimer)
  saveTimer = setTimeout(persistNow, 500) // 原版 ScheduleWorkspaceProfileSave 的防抖语义
}

async function persistNow() {
  clearTimeout(saveTimer)
  try {
    await SetValue(SAVE_KEY, JSON.stringify(serializePlacements()))
  } catch (e) {
    console.error('[canvas] 保存摆放失败', e)
  }
}

/** 导入摆放记录（对应 LoadPlacements）：未覆盖的注册组件按三列瀑布流补默认位置。 */
function loadPlacements(list) {
  const known = new Set()
  for (const p of list ?? []) {
    const def = definitions.find((d) => d.id.toLowerCase() === String(p.componentId ?? '').toLowerCase())
    if (!def || known.has(def.id)) continue
    known.add(def.id)
    addShell(def, p.relativeX, p.relativeY, p.zIndex, sanitizeSizeScale(p.sizeScale), p.rotationDegrees ?? 0)
  }
  let next = 0
  for (const def of definitions) {
    if (known.has(def.id)) continue
    addShell(def, 0.03 + (next % 3) * 0.33, 0.05 + Math.floor(next / 3) * 0.3)
    next++
  }
}

function sanitizeSizeScale(v) {
  const n = Number(v)
  return n > 0.05 && n <= 4 ? n : 1 // 旧档案缺失字段兜底（SanitizeSizeScale）
}

// ---- 添加 / 删除 ----
function addShell(def, relX = 0.32, relY = 0.3, z = null, sizeScale = 1, rotation = 0) {
  if (shells.value.some((s) => s.id === def.id)) return null
  const shell = reactive({
    id: def.id, def, relX, relY,
    z: z ?? ++maxZ, sizeScale: sanitizeSizeScale(sizeScale), rotation,
    removing: false,
  })
  shells.value.push(shell)
  schedulePersist()
  return shell
}

async function removeShell(s) {
  s.removing = true // 退场：缩小淡出（240ms accelerate，原版 M3 曲线）
  await new Promise((r) => setTimeout(r, 240))
  shells.value = shells.value.filter((x) => x !== s)
  schedulePersist()
}

async function removeWithConfirm(s) {
  if (await confirm('删除组件', `确定要从画布移除「${s.def.title}」吗？`)) {
    await removeShell(s)
  }
}

function raiseShell(s) {
  s.z = ++maxZ
  schedulePersist()
}

// ---- 指针拖拽（move / resize / rotate；指针捕获语义用 window 监听等价实现） ----
const drag = ref(null) // { mode, shellId, shell, ... }
const deleteZoneVisible = computed(() => drag.value != null)
const deleteHot = ref(false)

function canvasPoint(e) {
  const r = canvasEl.value.getBoundingClientRect()
  return { x: e.clientX - r.left, y: e.clientY - r.top }
}

function beginDrag(payload) {
  drag.value = payload
  window.addEventListener('pointermove', onPointerMove)
  window.addEventListener('pointerup', onPointerUp, { once: true })
  window.addEventListener('pointercancel', onPointerUp, { once: true })
}

function startMove(s, e) {
  if (e.button !== 0) return
  e.stopPropagation()
  raiseShell(s)
  const p = canvasPoint(e)
  const { x, y } = shellPx(s)
  beginDrag({ mode: 'move', shellId: s.id, shell: s, grabX: p.x - x, grabY: p.y - y, moved: false, startX: e.clientX, startY: e.clientY, liveX: x, liveY: y })
}

function startResize(s, e) {
  if (e.button !== 0) return
  e.stopPropagation()
  raiseShell(s)
  const p = canvasPoint(e)
  beginDrag({ mode: 'resize', shellId: s.id, shell: s, startX: p.x, startY: p.y, startScale: s.sizeScale })
}

function startRotate(s, e) {
  if (e.button !== 0) return
  e.stopPropagation()
  raiseShell(s)
  beginDrag({ mode: 'rotate', shellId: s.id, shell: s })
}

function onDeleteZonePoint(p) {
  if (!deleteZoneVisible.value) return false
  // 垃圾桶圆心 ≈ (canvas.width/2, canvas.height - 88)，半径 30 + 6 容差（原版同款）
  const cx = canvasSize.width / 2
  const cy = canvasSize.height - 88
  const dx = p.x - cx
  const dy = p.y - cy
  return dx * dx + dy * dy <= 36 * 36
}

function onPointerMove(e) {
  const d = drag.value
  if (!d) return
  const p = canvasPoint(e)
  const s = d.shell

  if (d.mode === 'move') {
    if (!d.moved && Math.hypot(e.clientX - d.startX, e.clientY - d.startY) < DRAG_START_THRESHOLD) return
    d.moved = true
    const { w, h } = shellPx(s)
    const availW = Math.max(0, canvasSize.width - w)
    const availH = Math.max(0, canvasSize.height - h)
    const x = Math.round(Math.min(Math.max(0, p.x - d.grabX), availW) / SNAP_GRID) * SNAP_GRID
    const y = Math.round(Math.min(Math.max(0, p.y - d.grabY), availH) / SNAP_GRID) * SNAP_GRID
    // 归一化坐标同步更新，落位即最终值（MoveShellLive 语义）
    s.relX = availW <= 0 ? 0 : Math.min(1, Math.max(0, x / availW))
    s.relY = availH <= 0 ? 0 : Math.min(1, Math.max(0, y / availH))
    d.liveX = x
    d.liveY = y
    deleteHot.value = onDeleteZonePoint(p)
  } else if (d.mode === 'resize') {
    // 取水平 / 垂直位移中较大的一个作为等比缩放驱动量（原版 AttachResizeGrip 语义）
    const delta = Math.max(p.x - d.startX, p.y - d.startY)
    const startW = s.def.preferredWidth * d.startScale
    const newSize = Math.min(
      Math.max(startW + delta, s.def.preferredWidth * MIN_SIZE_SCALE),
      s.def.preferredWidth * MAX_SIZE_SCALE,
    )
    s.sizeScale = newSize / s.def.preferredWidth
  } else if (d.mode === 'rotate') {
    // 指针相对组件中心的角度即旋转角，15° 吸附（原版 AttachRotateGrip 语义）
    const { x, y, w, h } = shellPx(s)
    const angle = (Math.atan2(p.y - (y + h / 2), p.x - (x + w / 2)) * 180) / Math.PI + 90
    let snapped = Math.round(angle / ROTATION_SNAP_DEGREES) * ROTATION_SNAP_DEGREES
    snapped = snapped >= 180 ? snapped - 360 : snapped <= -180 ? snapped + 360 : snapped
    s.rotation = snapped
  }
}

async function onPointerUp(e) {
  window.removeEventListener('pointermove', onPointerMove)
  const d = drag.value
  drag.value = null
  deleteHot.value = false
  if (!d || d.mode !== 'move' || !d.moved) return
  const p = canvasPoint(e)

  // 拖到底部垃圾桶 = 删除（先判命中再隐藏，原版 EndMoveDrag 注释强调的顺序）
  if (onDeleteZonePoint(p)) {
    if (await confirm('删除组件', `确定要删除「${d.shell.def.title}」吗？`)) {
      await removeShell(d.shell)
    }
    return
  }
  schedulePersist()
  // 落位回弹：原版 BounceAsync(root, 1.03, 180) 由下方 settle 动画近似
}

// 吸附参考线：拖动中显示组件左缘 / 上缘所在位置
const guideVStyle = computed(() => ({ left: `${drag.value?.liveX ?? 0}px` }))
const guideHStyle = computed(() => ({ top: `${drag.value?.liveY ?? 0}px` }))

// ---- 组件库：点击添加 / 按下即拖（ComponentLibraryView 拖出语义） ----
const libraryOpen = ref(false)
const libDrag = ref(null) // { def, moved, startX, startY, x, y }

function startLibraryDrag(def, e) {
  if (e.button !== 0 || placedIds.value.has(def.id)) return
  libDrag.value = { def, moved: false, startX: e.clientX, startY: e.clientY, x: e.clientX, y: e.clientY }
  window.addEventListener('pointermove', onLibPointerMove)
  window.addEventListener('pointerup', onLibPointerUp, { once: true })
  window.addEventListener('pointercancel', onLibPointerUp, { once: true })
}

function onLibPointerMove(e) {
  const d = libDrag.value
  if (!d) return
  d.x = e.clientX
  d.y = e.clientY
  if (!d.moved && Math.hypot(e.clientX - d.startX, e.clientY - d.startY) >= DRAG_START_THRESHOLD) {
    d.moved = true
    libraryOpen.value = false // 原版语义：拖拽启动即收起抽屉
  }
}

function onLibPointerUp(e) {
  window.removeEventListener('pointermove', onLibPointerMove)
  const d = libDrag.value
  libDrag.value = null
  if (!d) return
  const canvasRect = canvasEl.value?.getBoundingClientRect()
  if (!canvasRect) return
  const overCanvas = e.clientX >= canvasRect.left && e.clientX <= canvasRect.right
    && e.clientY >= canvasRect.top && e.clientY <= canvasRect.bottom
  if (!overCanvas) return

  if (d.moved) {
    // 拖入：落点即组件中心（原版 GetRelativeCentered 语义）
    const w = d.def.preferredWidth
    const h = d.def.preferredHeight
    const availW = Math.max(1, canvasSize.width - w)
    const availH = Math.max(1, canvasSize.height - h)
    const relX = Math.min(1, Math.max(0, (e.clientX - canvasRect.left - w / 2) / availW))
    const relY = Math.min(1, Math.max(0, (e.clientY - canvasRect.top - h / 2) / availH))
    addShell(d.def, relX, relY)
  } else {
    // 点击：默认位置（原版 AddRequested → AddComponent 默认 0.32/0.30）
    addShell(d.def)
  }
}

function closeLibrary() {
  libraryOpen.value = false
}

// 进入编辑模式自动打开组件库抽屉（原版 EnterEditMode 语义）
let lastEditing = editing.value
function watchEditing() {
  if (editing.value && !lastEditing) libraryOpen.value = true
  lastEditing = editing.value
  editingWatcherId = requestAnimationFrame(watchEditing)
}
let editingWatcherId = 0

const libPreviewStyle = computed(() => {
  const d = libDrag.value
  if (!d) return {}
  return {
    left: `${d.x - d.def.preferredWidth * globalScale.value / 2}px`,
    top: `${d.y - d.def.preferredHeight * globalScale.value / 2}px`,
    width: `${d.def.preferredWidth * globalScale.value}px`,
    height: `${d.def.preferredHeight * globalScale.value}px`,
  }
})

onMounted(async () => {
  reloadGlobalScale()
  window.addEventListener('nya:personalization', reloadGlobalScale)
  window.addEventListener('storage', reloadGlobalScale)
  resizeObserver = new ResizeObserver((entries) => {
    const r = entries[0].contentRect
    canvasSize.width = r.width
    canvasSize.height = r.height
  })
  resizeObserver.observe(canvasEl.value)
  watchEditing()

  // 恢复摆放（GetValue 无值时返回空串 → 走瀑布流默认位置）
  let savedPlacements = []
  try {
    const raw = await GetValue(SAVE_KEY)
    if (raw) savedPlacements = JSON.parse(raw)
  } catch (e) {
    console.error('[canvas] 读取摆放失败（开发模式无后端时属预期）', e)
  }
  loadPlacements(savedPlacements)
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  cancelAnimationFrame(editingWatcherId)
  window.removeEventListener('nya:personalization', reloadGlobalScale)
  window.removeEventListener('storage', reloadGlobalScale)
  window.removeEventListener('pointermove', onPointerMove)
  window.removeEventListener('pointermove', onLibPointerMove)
  persistNow()
})
</script>

<style scoped>
/* 编辑模式 16px 吸附网格可视化 */
.canvas-grid {
  background-image:
    linear-gradient(to right, var(--default-border) 1px, transparent 1px),
    linear-gradient(to bottom, var(--default-border) 1px, transparent 1px);
  background-size: 16px 16px;
  opacity: 0.35;
}

/* 组件入场：上浮淡入（SlideFadeInAsync 220ms / 12px 等价） */
.shell {
  animation: shell-in 220ms var(--ease-emphasized-decelerate) both;
}

@keyframes shell-in {
  from {
    opacity: 0;
    translate: 0 12px;
  }
  to {
    opacity: 1;
    translate: 0 0;
  }
}

/* 组件库抽屉滑入（DrawerWidth 0→400 的等价过渡，300ms decelerate） */
.drawer-in {
  animation: drawer-in 300ms var(--ease-emphasized-decelerate) both;
}

@keyframes drawer-in {
  from { translate: -100% 0; }
  to { translate: 0 0; }
}
</style>
