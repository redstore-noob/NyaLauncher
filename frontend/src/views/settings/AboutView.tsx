/*
 * AboutSettingsView —— 等价 Vue 版 settings/About.vue，还原 AboutPage：
 * 贡献者名单 / 引用的库与资源 / 项目信息 / QQ 群 / 猫娘彩蛋。
 * 真实绑定：SystemAPI.GetFormattedVersion（版本串）、GetAppVersion（补丁日期行）、ClearLogs。
 * 开源许可与第三方组件文案照抄原 AboutPage.axaml。
 * 猫娘彩蛋覆盖层：连点贡献者卡片 7 次（2 秒内）触发，点击任意处关闭（createPortal 至 body）。
 */
import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { ClearLogs, GetAppVersion, GetFormattedVersion } from '../../../wailsjs/go/bindings/SystemAPI.js'
import { Button } from '@/components/ui/button'
import './settings-shared.css'

const QQ_GROUP_NUMBER = '1108330006'
const contributors = [
  { name: 'Mystic Stars', url: 'https://github.com/Mystic-Stars' },
  { name: 'TouristH', url: 'https://github.com/TouristH' },
]
const libs = [
  { name: 'Avalonia UI', desc: '12.1.1 · 跨平台 UI 框架（MIT License） https://github.com/AvaloniaUI/Avalonia' },
  { name: 'Material.Avalonia', desc: '3.19.0 · Material Design 风格与控件库（MIT License） https://github.com/AvaloniaCommunity/Material.Avalonia' },
  { name: 'Material.Icons.Avalonia', desc: '3.0.2 · Material Design 图标库（MIT License） https://github.com/SKProCH/Material.Icons' },
  { name: 'CommunityToolkit.Mvvm', desc: '8.4.2 · 现代化 MVVM 工具包（MIT License） https://github.com/CommunityToolkit/dotnet' },
  { name: 'NAudio', desc: '2.2.1 · 音乐播放器音频后端（MS-PL License） https://github.com/naudio/NAudio' },
  { name: 'OpenAI .NET', desc: '2.13.0 · AI 对话接口调用（OpenAI 官方 SDK · Apache-2.0） https://github.com/openai/openai-dotnet' },
  { name: 'BMCLAPI', desc: 'Minecraft 下载镜像服务（bangbang93） https://bmclapi2.bangbang93.com' },
]

/* 猫娘彩蛋：连点 7 次（2 秒内）触发 */
const NEKO_TRIGGER_CLICKS = 7

export default function AboutView() {
  const [versionText, setVersionText] = useState('')
  const [patchDate, setPatchDate] = useState('')
  const [clearingLogs, setClearingLogs] = useState(false)
  const [clearLogsHint, setClearLogsHint] = useState('')
  const [easterEggHint, setEasterEggHint] = useState('')
  const [nekoVisible, setNekoVisible] = useState(false)

  const clickCount = useRef(0)
  const resetTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  async function clearLogs() {
    setClearingLogs(true)
    try {
      const removed = await ClearLogs()
      setClearLogsHint(removed > 0 ? `已清理 ${removed} 个日志文件。` : '没有可清理的日志。')
    } catch (err) {
      setClearLogsHint(`清理失败：${(err as Error)?.message || err}`)
    } finally {
      setClearingLogs(false)
    }
  }

  function onContributorsClick() {
    clickCount.current++
    if (resetTimer.current) clearTimeout(resetTimer.current)
    resetTimer.current = setTimeout(() => {
      clickCount.current = 0
      setEasterEggHint('')
    }, 2000)
    if (clickCount.current < NEKO_TRIGGER_CLICKS) {
      setEasterEggHint(`连点贡献者卡片有惊喜（还需 ${NEKO_TRIGGER_CLICKS - clickCount.current} 次）`)
      return
    }
    clickCount.current = 0
    if (resetTimer.current) clearTimeout(resetTimer.current)
    setEasterEggHint('')
    setNekoVisible(true)
  }

  function openLink(url: string) {
    window.open(url, '_blank', 'noopener')
  }

  async function copyGroup() {
    try {
      await navigator.clipboard.writeText(QQ_GROUP_NUMBER)
      setEasterEggHint(`群号已复制：${QQ_GROUP_NUMBER}，欢迎来玩喵~`)
      setTimeout(() => setEasterEggHint(''), 2000)
    } catch {
      setEasterEggHint(`剪贴板不可用，请手动记录群号：${QQ_GROUP_NUMBER}`)
    }
  }

  useEffect(() => {
    GetFormattedVersion().then(setVersionText).catch(() => { /* 绑定不可用时保持静态文案 */ })
    GetAppVersion().then(setPatchDate).catch(() => { /* 同上 */ })
    return () => { if (resetTimer.current) clearTimeout(resetTimer.current) }
  }, [])

  return (
    <section className="page px-10 pt-10 pb-8">
      <header className="page-head">
        <h1 className="m-0 text-[28px] font-bold text-primary-text">关于</h1>
        <p className="m-0 text-[13px] text-hint-text">NyaLauncher 开发团队、第三方组件与项目信息</p>
      </header>

      <div className="cards">
        {/* 贡献者名单：连点卡片 7 次（2 秒内）触发猫娘彩蛋 */}
        <section className="about-card" onClick={onContributorsClick}>
          <h3 className="m-0 text-[18px] font-semibold text-primary-text">贡献者名单</h3>
          <div className="flex flex-wrap gap-2">
            {contributors.map((c) => (
              <button
                key={c.url}
                className="inline-flex cursor-pointer items-center gap-1.5 rounded-full border-none bg-badge px-3 py-1.5 text-[12px] font-semibold text-primary transition-[filter,transform] duration-150 hover:brightness-95 active:scale-95"
                onClick={(e) => { e.stopPropagation(); openLink(c.url) }}
              >
                <span className="text-[13px]">🤝</span>{c.name}
              </button>
            ))}
          </div>
          <p className="m-0 text-[11px] text-hint-text">感谢每一位通过 PR / Issue 为项目出力的伙伴，点个名字就能去 TA 的主页看看喵～</p>
          {easterEggHint ? <p className="m-0 text-[11px] text-primary">{easterEggHint}</p> : null}
        </section>

        {/* 引用的库与资源（文案照抄原 AboutPage.axaml） */}
        <section className="about-card">
          <h3 className="m-0 text-[18px] font-semibold text-primary-text">引用的库与资源</h3>
          <div className="grid grid-cols-[160px_1fr] gap-x-2 gap-y-2.5">
            {libs.map((lib) => (
              <FragmentPair key={lib.name} name={lib.name} desc={lib.desc} />
            ))}
          </div>
        </section>

        {/* 项目信息 */}
        <section className="about-card">
          <h3 className="m-0 text-[18px] font-semibold text-primary-text">项目信息</h3>
          <p className="m-0 text-[14px] leading-relaxed text-body-text">{versionText || 'NyaLauncher'}</p>
          <p className="m-0 text-[14px] leading-relaxed text-body-text">最后补丁日期: {patchDate}</p>
          <p className="m-0 text-[14px] leading-relaxed text-body-text">该软件基于 Apache License 2.0 分发。</p>
          <p className="m-0 text-[14px] leading-relaxed text-body-text">该软件为NyaLauncher team编写，与Mojang Studios与Microsoft没有直接联系。</p>
          <p className="m-0 text-[11px] text-hint-text">https://github.com/redstore-noob/NyaLauncher</p>
          <Button variant="link" className="self-start px-0" onClick={() => openLink('https://github.com/redstore-noob/NyaLauncher')}>
            项目链接喵~Ciallo～(∠・ω&lt; )⌒★
          </Button>
          <div className="flex items-center gap-2">
            <Button variant="secondary" size="sm" disabled={clearingLogs} onClick={clearLogs}>清理日志</Button>
            {clearLogsHint ? <span className="text-[11px] text-hint-text">{clearLogsHint}</span> : null}
          </div>
        </section>

        {/* QQ 群 */}
        <section className="about-card flex-row items-center justify-between gap-4">
          <div className="flex flex-col gap-1">
            <h3 className="m-0 text-[18px] font-semibold text-primary-text">加入我们</h3>
            <p className="m-0 text-[14px] leading-relaxed text-body-text">QQ 群：1108330006 · 反馈建议 · 版本抢先体验 · 日常摸鱼</p>
          </div>
          <Button onClick={copyGroup}>复制群号</Button>
        </section>
      </div>

      {/* 猫娘彩蛋覆盖层：连点贡献者卡片 7 次触发；点击任意处关闭（Teleport to body 等价） */}
      {nekoVisible
        ? createPortal(
          <div className="fixed inset-0 z-[1000] flex items-center justify-center bg-overlay" onClick={() => setNekoVisible(false)}>
            <div className="neko-card flex max-w-[420px] flex-col items-center gap-2.5 rounded-2xl border border-default-border bg-card p-4">
              <div className="text-[72px] leading-tight">🐱🎀</div>
              <p className="m-0 text-[15px] font-semibold text-primary-text">奈娅(Nya)美图🥰</p>
              <p className="m-0 text-[11px] text-hint-text">PORTING_NOTES：原版彩蛋图片（neko-girl.png）为 Avalonia 资源，前端资源未随移植，先以占位呈现。</p>
              <p className="m-0 text-[11px] text-hint-text">点击任意处关闭</p>
            </div>
          </div>,
          document.body,
        )
        : null}
    </section>
  )
}

/* 引用库的 grid 两列对（React 片段辅助组件，等价 Vue 版 template v-for 内的双 span） */
function FragmentPair({ name, desc }: { name: string; desc: string }) {
  return (
    <>
      <span className="self-center text-[13px] text-muted-foreground">{name}</span>
      <span className="whitespace-pre-line text-[13px] leading-relaxed text-body-text">{desc}</span>
    </>
  )
}
