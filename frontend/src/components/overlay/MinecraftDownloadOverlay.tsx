/*
 * MinecraftDownloadOverlay.tsx（移植自 Controls/MinecraftDownloadOverlay.axaml，参照
 * Vue 版 MinecraftDownloadOverlay.vue）：版本下载确认遮罩 —— Loader 类型卡片 /
 * Loader 版本 / 实例名 / 状态提示 / 取消+下载。radix Dialog 承载（等价 ModalOverlayHost）。
 *
 * 逻辑与 Vue 版一一对应：
 * - Loader 元数据经 DownloadAPI.GetModLoaderVersions（枚举索引与 Go ModLoaderType 对齐：
 *   Vanilla=0 / Fabric=1 / Quilt=2 / NeoForge=3 / Forge=4）；
 * - 默认实例名经 DownloadAPI.CreateDefaultInstanceName；
 * - 确认时 onConfirm({ loaderType, loaderVersion, instanceName, skipFabricApi })，
 *   由 Download 页决定 StartDownload / StartModLoaderDownload。
 */
import { useEffect, useRef, useState } from 'react'
import { ArrowDown, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { CreateDefaultInstanceName, GetModLoaderVersions } from '../../../wailsjs/go/bindings/DownloadAPI.js'
import type { models, download } from '../../../wailsjs/go/models.js'

interface ConfirmOptions {
  loaderType: number
  loaderVersion: download.ModLoaderVersion | null
  instanceName: string
  skipFabricApi: boolean
}

interface Props {
  version: models.MinecraftVersion | null
  onClose: () => void
  onConfirm: (options: ConfirmOptions) => void
}

const loaderTypes = [
  { value: 0, label: '原版' },
  { value: 1, label: 'Fabric' },
  { value: 3, label: 'NeoForge' },
  { value: 4, label: 'Forge' },
]

function typeDisplay(t: string): string {
  return ({ release: '正式版', snapshot: '快照版', old_alpha: '远古版本', old_beta: '远古版本' } as Record<string, string>)[t] ?? t
}

// OverlayHelpers.IsValidInstanceName：拒绝空 / "." ".." / 非法文件名字符 / 路径分隔符
function validateInstanceName(name: string): string {
  if (!name || !name.trim()) return '请输入实例名称。'
  if (name === '.' || name === '..') return '实例名称非法。'
  if (/[\\/:*?"<>|]/.test(name)) return '实例名包含不安全字符，请换一个名字。'
  return ''
}

export default function MinecraftDownloadOverlay({ version, onClose, onConfirm }: Props) {
  const [loaderType, setLoaderType] = useState(0)
  const [loaderVersions, setLoaderVersions] = useState<download.ModLoaderVersion[]>([])
  const [loaderVersionIndex, setLoaderVersionIndex] = useState('')
  const [loaderLoading, setLoaderLoading] = useState(false)
  const [loaderHint, setLoaderHint] = useState('')
  const [skipFabricApi, setSkipFabricApi] = useState(false)
  const [instanceName, setInstanceName] = useState('')
  const [instanceNameHint, setInstanceNameHint] = useState('')
  const [statusText, setStatusText] = useState('')

  // 快速切换加载器时丢弃过期请求（等价 Vue 版 loadSeq）
  const loadSeq = useRef(0)

  // Setup(version)：version 变化时复位表单并预填实例名（等价 watch immediate）
  useEffect(() => {
    if (!version) return
    setLoaderType(0)
    setLoaderVersions([])
    setLoaderVersionIndex('')
    setLoaderHint('')
    setSkipFabricApi(false)
    setStatusText('')
    setInstanceName(version.id)
    setInstanceNameHint(`版本将安装至 versions/${version.id}/`)
  }, [version])

  // OnLoaderTypeChanged：拉取 Loader 元数据，优先选中稳定版
  async function applyLoaderType(t: number) {
    setLoaderType(t)
    setStatusText('')
    const mcId = version?.id
    if (!mcId) return
    if (t === 0) {
      setLoaderVersions([])
      setLoaderVersionIndex('')
      setLoaderHint('')
      setInstanceName(mcId)
      setInstanceNameHint(`版本将安装至 versions/${mcId}/`)
      return
    }
    const seq = ++loadSeq.current
    setLoaderLoading(true)
    setLoaderVersions([])
    setLoaderVersionIndex('')
    setLoaderHint('正在获取可用版本…')
    try {
      const list = (await GetModLoaderVersions(t as never, mcId)) ?? []
      if (seq !== loadSeq.current) return // 快速切换时丢弃旧结果
      setLoaderVersions(list)
      if (list.length > 0) {
        const preferred = list.findIndex((v) => v.IsStable)
        void selectLoaderVersion(t, preferred >= 0 ? preferred : 0)
      } else {
        setLoaderHint('该 Minecraft 版本暂无可用的加载器版本。')
      }
    } catch (e) {
      if (seq !== loadSeq.current) return
      setLoaderHint(`获取版本列表失败：${(e as Error)?.message ?? e}`)
    } finally {
      if (seq === loadSeq.current) setLoaderLoading(false)
    }
  }

  async function selectLoaderType(t: number) {
    if (t === loaderType) return
    await applyLoaderType(t)
  }

  async function selectLoaderVersion(t: number, i: number) {
    const lv = loaderVersions[i]
    if (!lv) return
    setLoaderVersionIndex(String(i))
    setLoaderHint(lv.IsStable ? '推荐版本' : '非稳定版本，可能存在兼容性问题')
    try {
      // CreateDefaultInstanceName(loaderType, loaderVersion, mcVersion)
      const name = await CreateDefaultInstanceName(t as never, lv.LoaderVersion, version?.id ?? '')
      setInstanceName(name)
      setInstanceNameHint(`版本将安装至 versions/${name}/`)
    } catch (e) {
      console.error('生成默认实例名失败', e)
    }
  }

  function onLoaderVersionChange(idx: string) {
    setLoaderVersionIndex(idx)
    const i = Number(idx)
    if (!Number.isNaN(i)) void selectLoaderVersion(loaderType, i)
  }

  function onDownload() {
    if (!version) return
    let loaderVersion: download.ModLoaderVersion | null = null
    if (loaderType !== 0) {
      const i = Number(loaderVersionIndex)
      loaderVersion = loaderVersions[i] ?? null
      if (!loaderVersion) {
        setStatusText('请选择加载器版本。')
        return
      }
    }
    const fallback = loaderVersion ? `${version.id}-${loaderVersion.LoaderVersion}` : version.id
    const name = (instanceName || '').trim() || fallback
    const nameErr = validateInstanceName(name)
    if (nameErr) {
      setStatusText(nameErr)
      return
    }
    onConfirm({ loaderType, loaderVersion, instanceName: name, skipFabricApi })
  }

  return (
    <Dialog open={version !== null} onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent showCloseButton={false} className="max-h-[620px] w-[540px] max-w-[540px] gap-[14px] rounded-2xl px-6 pb-[22px] pt-5">
        <DialogTitle className="sr-only">下载 Minecraft</DialogTitle>

        {/* 标题栏（OverlayHeader.axaml：44 徽标 + 标题/副标题 + 圆形关闭钮） */}
        <div className="flex items-center gap-3">
          <div className="flex size-11 shrink-0 items-center justify-center rounded-[10px] bg-badge text-[20px]">⛏</div>
          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <span className="truncate text-base font-bold text-foreground">下载 Minecraft</span>
            <span className="truncate text-[11px] text-hint-text">
              {version ? `${version.id}（${typeDisplay(version.type)}）` : ''}
            </span>
          </div>
          <button
            className="flex size-[30px] shrink-0 cursor-pointer items-center justify-center rounded-full bg-secondary text-subtext-text transition-opacity hover:opacity-[var(--hover-opacity)]"
            onClick={onClose}
          >
            <X size={14} />
          </button>
        </div>

        {/* Loader 类型（RadioButton.LoaderCard：ControlBg r10 卡片，选中 HighlightBg + Accent 边） */}
        <div className="flex flex-col gap-2">
          <span className="text-[13px] font-semibold text-secondary-text">选择加载器</span>
          <div className="grid grid-cols-4 gap-2">
            {loaderTypes.map((lt) => (
              <button
                key={lt.value}
                className={`cursor-pointer rounded-[10px] border border-transparent bg-muted px-2.5 py-[9px] text-[13px] font-semibold text-body-text transition-colors ${
                  loaderType === lt.value ? 'border-border bg-accent !text-primary' : 'hover:bg-accent'
                }`}
                onClick={() => void selectLoaderType(lt.value)}
              >
                {lt.label}
              </button>
            ))}
          </div>
        </div>

        {/* Loader 版本（原版时隐藏；切换时淡入 200ms emphasized） */}
        {loaderType !== 0 && (
          <div className="nya-fade-in flex flex-col gap-2">
            <span className="text-[13px] font-semibold text-secondary-text">加载器版本</span>
            <Select value={loaderVersionIndex} onValueChange={onLoaderVersionChange} disabled={loaderLoading || loaderVersions.length === 0}>
              <SelectTrigger className="w-full rounded-lg border-none bg-muted px-3 py-[7px] text-[13px]">
                <SelectValue placeholder={loaderLoading ? '正在加载版本列表…' : (loaderVersions.length ? '选择加载器版本' : '无可用版本')} />
              </SelectTrigger>
              <SelectContent>
                {loaderVersions.map((lv, i) => (
                  <SelectItem key={lv.LoaderVersion} value={String(i)}>
                    {lv.LoaderVersion}{lv.IsStable ? '' : '（非稳定）'}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {loaderHint && <span className="text-[11px] leading-relaxed text-hint-text">{loaderHint}</span>}
            {loaderType === 1 && (
              <label className="flex cursor-pointer items-center gap-2 text-xs text-body-text">
                <Switch checked={skipFabricApi} onCheckedChange={setSkipFabricApi} />
                不主动下载 Fabric API
              </label>
            )}
          </div>
        )}

        {/* 实例名称 */}
        <div className="flex flex-col gap-2">
          <span className="text-[13px] font-semibold text-secondary-text">实例名称</span>
          <Input
            value={instanceName}
            onChange={(e) => setInstanceName(e.target.value)}
            placeholder="留空则使用默认名称"
            className="h-auto rounded-lg border-none bg-muted px-3 py-2 text-[13px]"
          />
          {instanceNameHint && <span className="text-[11px] leading-relaxed text-hint-text">{instanceNameHint}</span>}
        </div>

        {/* 状态提示（错误） */}
        {statusText && <span className="text-xs leading-relaxed text-destructive">{statusText}</span>}

        {/* 底部按钮：取消 / 下载 */}
        <div className="flex justify-end gap-2">
          <Button variant="secondary" className="h-auto px-[18px] py-2 text-[13px]" onClick={onClose}>取消</Button>
          <Button className="h-auto gap-1.5 px-5 py-2 text-[13px] font-semibold" onClick={onDownload}>
            <ArrowDown size={15} /><span>下载</span>
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}
