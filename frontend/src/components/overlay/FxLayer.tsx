/*
 * 个性化特效层（PersonalizationSettingsPage 的 Avalonia 附加特效层移植，React 版）：
 * - 自定义背景图：全局最底层壁纸（固定定位 -z，随不透明度/模糊值渲染）；
 * - 彩虹背景（AmbientGradient）：窗口底部主题色渐变氛围层（hue 循环动画）；
 * - 星尘（SparkleTrail）：鼠标移动的 canvas 粒子拖尾；
 * - 点击圆环（ClickRing）：全局 click 扩散环。
 * 开关/背景数据均在 settings/Personalization 写入 localStorage，本层只读消费；
 * 该页保存后派发 nya:personalization 事件，这里即时跟随（免刷新）。
 */
import { useEffect, useRef, useState } from 'react'

const FX_KEY = 'nyalauncher.fx'
const BG_KEY = 'nyalauncher.customBg'

interface FxState { ambient: boolean; sparkle: boolean; clickRing: boolean }
interface BgState { path: string; dataUrl: string; opacity: number; blur: number }

interface Ring { id: number; x: number; y: number }

export default function FxLayer() {
  const [fx, setFx] = useState<FxState>({ ambient: false, sparkle: false, clickRing: false })
  const [bg, setBg] = useState<BgState>({ path: '', dataUrl: '', opacity: 0.3, blur: 0 })
  const [bgFailed, setBgFailed] = useState(false)
  const [rings, setRings] = useState<Ring[]>([])

  const sparkleRef = useRef<HTMLCanvasElement | null>(null)
  const fxRef = useRef(fx)
  fxRef.current = fx
  const ringsRef = useRef<Ring[]>([])
  ringsRef.current = rings
  const setRingsSafe = setRings

  const bgUrl = bg.dataUrl || (bg.path && !bgFailed ? `/localfile?path=${encodeURIComponent(bg.path)}` : '')

  // ---- 星尘粒子（canvas，鼠标移动生成、逐帧衰减） ----
  useEffect(() => {
    let ctx: CanvasRenderingContext2D | null = null
    let particles: Array<{ x: number; y: number; vx: number; vy: number; life: number; size: number; hue: number }> = []
    let rafId = 0
    let lastMove = 0

    function resizeCanvas() {
      const el = sparkleRef.current
      if (!el) return
      el.width = el.clientWidth * devicePixelRatio
      el.height = el.clientHeight * devicePixelRatio
      ctx = el.getContext('2d')
    }

    function onPointerMove(e: PointerEvent) {
      if (!fxRef.current.sparkle) return
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
      const el = sparkleRef.current
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
    let ringSeq = 0
    function onClick(e: MouseEvent) {
      if (!fxRef.current.clickRing || e.button !== 0) return
      const id = ++ringSeq
      setRingsSafe((rs) => [...rs, { id, x: e.clientX, y: e.clientY }])
      setTimeout(() => { setRingsSafe((rs) => rs.filter((r) => r.id !== id)) }, 650)
    }

    function loadState() {
      let nextFx: Partial<FxState> = {}
      let nextBg: Partial<BgState> = {}
      try { nextFx = JSON.parse(localStorage.getItem(FX_KEY) || '{}') } catch { /* ignore */ }
      try { nextBg = JSON.parse(localStorage.getItem(BG_KEY) || '{}') } catch { /* ignore */ }
      setFx({ ambient: false, sparkle: false, clickRing: false, ...nextFx })
      setBg({ path: '', dataUrl: '', opacity: 0.3, blur: 0, ...nextBg })
      setBgFailed(false)
      if (nextFx.sparkle) resizeCanvas()
    }

    function onPersonalizationChanged() { loadState() }

    loadState()
    window.addEventListener('nya:personalization', onPersonalizationChanged)
    window.addEventListener('storage', onPersonalizationChanged)
    window.addEventListener('pointermove', onPointerMove)
    window.addEventListener('click', onClick)
    window.addEventListener('resize', resizeCanvas)
    tick()
    return () => {
      cancelAnimationFrame(rafId)
      window.removeEventListener('nya:personalization', onPersonalizationChanged)
      window.removeEventListener('storage', onPersonalizationChanged)
      window.removeEventListener('pointermove', onPointerMove)
      window.removeEventListener('click', onClick)
      window.removeEventListener('resize', resizeCanvas)
    }
  }, [setRingsSafe])

  return (
    <div className="pointer-events-none fixed inset-0 z-[-1]" aria-hidden="true">
      {/* 自定义背景图（key: nyalauncher.customBg） */}
      {bgUrl ? (
        <img
          src={bgUrl}
          alt=""
          className="absolute inset-0 h-full w-full object-cover"
          style={{ opacity: bg.opacity, filter: `blur(${bg.blur}px)`, transform: bg.blur > 0 ? 'scale(1.03)' : undefined }}
          onError={() => setBgFailed(true)}
        />
      ) : null}

      {/* 彩虹背景：窗口底部渐变氛围层 */}
      {fx.ambient ? <div className="rainbow-layer absolute inset-x-0 bottom-0 h-[38vh]" /> : null}

      {/* 星尘拖尾画布 */}
      {fx.sparkle ? <canvas ref={sparkleRef} className="absolute inset-0 h-full w-full" /> : null}

      {/* 点击圆环容器 */}
      {fx.clickRing
        ? rings.map((ring) => (
          <span
            key={ring.id}
            className="click-ring absolute rounded-full border-2"
            style={{ left: `${ring.x}px`, top: `${ring.y}px` }}
          />
        ))
        : null}
    </div>
  )
}
