<template>
  <!--
    个性化特效层（PersonalizationSettingsPage 的 Avalonia 附加特效层移植）：
    - 自定义背景图：全局最底层壁纸（固定定位 -z，随不透明度/模糊值渲染）；
    - 彩虹背景（AmbientGradient）：窗口底部主题色渐变氛围层（hue 循环动画）；
    - 星尘（SparkleTrail）：鼠标移动的 canvas 粒子拖尾；
    - 点击圆环（ClickRing）：全局 click 扩散环。
    开关/背景数据均在 settings/Personalization.vue 写入 localStorage，本层只读消费；
    该页保存后派发 nya:personalization 事件，这里即时跟随（免刷新）。
  -->
  <div class="pointer-events-none fixed inset-0 z-[-1]" aria-hidden="true">
    <!-- 自定义背景图（key: nyalauncher.customBg） -->
    <img
      v-if="bgUrl"
      :src="bgUrl"
      alt=""
      class="absolute inset-0 h-full w-full object-cover"
      :style="{ opacity: bg.opacity, filter: `blur(${bg.blur}px)`, transform: bg.blur > 0 ? 'scale(1.03)' : '' }"
      @error="bgFailed = true"
    >

    <!-- 彩虹背景：窗口底部渐变氛围层 -->
    <div v-if="fx.ambient" class="rainbow-layer absolute inset-x-0 bottom-0 h-[38vh]" />

    <!-- 星尘拖尾画布 -->
    <canvas v-if="fx.sparkle" ref="sparkleEl" class="absolute inset-0 h-full w-full" />

    <!-- 点击圆环容器 -->
    <template v-if="fx.clickRing">
      <span
        v-for="ring in rings"
        :key="ring.id"
        class="click-ring absolute rounded-full border-2"
        :style="{ left: `${ring.x}px`, top: `${ring.y}px` }"
      />
    </template>
  </div>
</template>

<script setup>
/*
 * 对应原版 Avalonia 的 AmbientGradient / SparkleTrail / ClickRing 特效控件；
 * 渲染层为前端等价实现（CSS 渐变动画 + canvas 粒子 + DOM 扩散环）。
 */
import { onBeforeUnmount, onMounted, reactive, ref } from 'vue'

const FX_KEY = 'nyalauncher.fx'
const BG_KEY = 'nyalauncher.customBg'

const fx = reactive({ ambient: false, sparkle: false, clickRing: false })
const bg = reactive({ path: '', dataUrl: '', opacity: 0.3, blur: 0 })
const bgFailed = ref(false)
const bgUrl = ref('')
const sparkleEl = ref(null)

const rings = ref([])
let ringSeq = 0

function loadState() {
  try { Object.assign(fx, JSON.parse(localStorage.getItem(FX_KEY)) || {}) } catch { /* ignore */ }
  try { Object.assign(bg, JSON.parse(localStorage.getItem(BG_KEY)) || {}) } catch { /* ignore */ }
  bgFailed.value = false
  bgUrl.value = bg.dataUrl || (bg.path && !bgFailed.value ? `/localfile?path=${encodeURIComponent(bg.path)}` : '')
}

// ---- 星尘粒子（canvas，鼠标移动生成、逐帧衰减） ----
let ctx = null
let particles = []
let rafId = 0
let lastMove = 0

function resizeCanvas() {
  const el = sparkleEl.value
  if (!el) return
  el.width = el.clientWidth * devicePixelRatio
  el.height = el.clientHeight * devicePixelRatio
  ctx = el.getContext('2d')
}

function onPointerMove(e) {
  if (!fx.sparkle) return
  const now = performance.now()
  if (now - lastMove < 16) return // ~60fps 限频
  lastMove = now
  for (let i = 0; i < 2; i++) {
    particles.push({
      x: e.clientX + (Math.random() - 0.5) * 10,
      y: e.clientY + (Math.random() - 0.5) * 10,
      vx: (Math.random() - 0.5) * 0.6,
      vy: -0.3 - Math.random() * 0.6,
      life: 1,
      size: 1.2 + Math.random() * 2,
      hue: Math.random() * 360,
    })
  }
  if (particles.length > 220) particles = particles.slice(-220)
}

function tick() {
  rafId = requestAnimationFrame(tick)
  const el = sparkleEl.value
  if (!el || !ctx) return
  ctx.clearRect(0, 0, el.width, el.height)
  if (particles.length === 0) return
  const dpr = devicePixelRatio
  particles = particles.filter((p) => p.life > 0.02)
  for (const p of particles) {
    p.x += p.vx
    p.y += p.vy
    p.life -= 0.022
    ctx.globalAlpha = Math.max(0, p.life) * 0.85
    ctx.fillStyle = `hsl(${p.hue}, 90%, 72%)`
    ctx.beginPath()
    ctx.arc(p.x * dpr, p.y * dpr, p.size * p.life * dpr, 0, Math.PI * 2)
    ctx.fill()
  }
  ctx.globalAlpha = 1
}

// ---- 点击圆环（扩散后自动移除） ----
function onClick(e) {
  if (!fx.clickRing || e.button !== 0) return
  const id = ++ringSeq
  rings.value.push({ id, x: e.clientX, y: e.clientY })
  setTimeout(() => { rings.value = rings.value.filter((r) => r.id !== id) }, 650)
}

function onPersonalizationChanged() {
  loadState()
  if (fx.sparkle) resizeCanvas()
}

onMounted(() => {
  loadState()
  window.addEventListener('nya:personalization', onPersonalizationChanged)
  window.addEventListener('storage', onPersonalizationChanged)
  window.addEventListener('pointermove', onPointerMove, { passive: true })
  window.addEventListener('click', onClick)
  window.addEventListener('resize', resizeCanvas)
  if (fx.sparkle) resizeCanvas()
  tick()
})

onBeforeUnmount(() => {
  cancelAnimationFrame(rafId)
  window.removeEventListener('nya:personalization', onPersonalizationChanged)
  window.removeEventListener('storage', onPersonalizationChanged)
  window.removeEventListener('pointermove', onPointerMove)
  window.removeEventListener('click', onClick)
  window.removeEventListener('resize', resizeCanvas)
})
</script>

<style scoped>
/* 彩虹氛围层：主题色系渐变 + hue 循环（对应 AmbientGradient，前端 CSS 等价实现） */
.rainbow-layer {
  background: linear-gradient(90deg,
    rgba(255, 0, 128, 0.16), rgba(255, 154, 0, 0.16), rgba(64, 255, 0, 0.14),
    rgba(0, 200, 255, 0.16), rgba(128, 0, 255, 0.16), rgba(255, 0, 128, 0.16));
  background-size: 300% 100%;
  animation: rainbow-slide 14s linear infinite;
  mask-image: linear-gradient(to top, black 30%, transparent);
  -webkit-mask-image: linear-gradient(to top, black 30%, transparent);
}

@keyframes rainbow-slide {
  from { background-position: 0% 50%; }
  to { background-position: 300% 50%; }
}

/* 点击扩散环：450ms 外扩 + 淡出 */
.click-ring {
  width: 14px;
  height: 14px;
  margin: -7px 0 0 -7px;
  border-color: var(--accent);
  opacity: 0.8;
  animation: click-ring-expand 650ms var(--ease-emphasized-decelerate) forwards;
}

@keyframes click-ring-expand {
  from { transform: scale(0.4); opacity: 0.85; }
  to { transform: scale(4.2); opacity: 0; }
}
</style>
