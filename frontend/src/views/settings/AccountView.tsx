/*
 * 账户管理页 —— 等价 Vue 版 settings/AccountView.vue（AccountManagePage.axaml 移植）。
 * 列表/默认账号与 AccountAPI 双向同步；添加账号复用 AccountLoginOverlay。
 * 头像经 GetAvatarUrl，失败回退首字母。
 * 更换皮肤：正版 SelectFile → UploadSkin（slim/classic 模型确认）；离线 SelectFile →
 * SetOfflineSkin，或经「从皮肤库选择」打开 GetOfflineSkinCatalog 内置皮肤网格。
 * PORTING（与 Vue 版一致）：披风不可用——绑定层暂未移植披风激活 API，保留提示。
 */
import { useCallback, useEffect, useState } from 'react'
import { ChevronDown } from 'lucide-react'
import Icon from '@/components/overlay/Icon'
import AccountLoginOverlay from '@/components/overlay/AccountLoginOverlay'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { alert, confirm as nyaConfirm } from '@/components/overlay/dialog'
import { SelectFile } from '../../../wailsjs/go/bindings/SystemAPI.js'
import {
  GetAccounts, GetSelectedAccount, GetAccountStableKey, RemoveAccount,
  MoveAccountToTop, GetAvatarUrl, UploadSkin, GetOfflineSkinCatalog, SetOfflineSkin,
} from '../../../wailsjs/go/bindings/AccountAPI.js'

// 与后端绑定模型保持同一类型（本地窄接口会让 GetAccountStableKey 等调用的参数类型不匹配）
import type { auth } from '../../../wailsjs/go/models'
type LaunchAccount = auth.LaunchAccount
interface AccountItem {
  account: LaunchAccount
  fallback: string
  isDefault: boolean
  stableKey: string
  avatarSource: string
}
interface SkinChoice {
  id: string
  source?: string
  fallbackText?: string
  displayName: string
  model: string
}

function typeLabel(account: LaunchAccount): string {
  switch (account.Type) {
    case 'microsoft': return '正版账户'
    case 'offline': return '离线账户'
    case 'authlib': return '皮肤站账户'
    default: return '第三方账户'
  }
}

function accountDetail(account: LaunchAccount): string {
  switch (account.Type) {
    case 'microsoft':
      if (account.Microsoft) {
        const raw = typeof account.Microsoft.ExpiresAt === 'number' && account.Microsoft.ExpiresAt > 1e15
          ? account.Microsoft.ExpiresAt / 1e6
          : account.Microsoft.ExpiresAt ?? 0
        const expired = new Date(raw).getTime() <= Date.now()
        return expired
          ? '正版账户 · 令牌已过期，启动游戏时会自动刷新'
          : `正版账户 · 令牌有效期至 ${formatTime(account.Microsoft.ExpiresAt)}`
      }
      return typeLabel(account)
    case 'authlib':
      if (account.Authlib) {
        return account.Authlib.ServerName
          ? `皮肤站账户 · ${account.Authlib.ServerName}`
          : `皮肤站账户 · ${account.Authlib.ApiRoot}`
      }
      return typeLabel(account)
    case 'offline': {
      const skin = account.OfflineSkinId || 'steve'
      // 自定义皮肤（本地 png 路径）只显示文件名
      const label = skin.toLowerCase().endsWith('.png') ? `自定义皮肤（${skin.split(/[\\/]/).pop()}）` : skin
      return `离线账户 · 默认皮肤：${label}`
    }
    default:
      return typeLabel(account)
  }
}

function formatTime(value: unknown): string {
  const date = new Date(typeof value === 'number' && value > 1e15 ? value / 1e6 : (value as string))
  if (Number.isNaN(date.getTime())) return String(value ?? '')
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function getFallbackText(value: string): string {
  if (!value || !value.trim()) return '?'
  return value.trim()[0].toUpperCase()
}

export default function AccountView() {
  const [items, setItems] = useState<AccountItem[]>([])
  const [selectedKey, setSelectedKey] = useState('')
  const [statusText, setStatusText] = useState('')
  const [showLogin, setShowLogin] = useState(false)
  // 头像缓存：stableKey → 头像 data URI
  const [avatarMap, setAvatarMap] = useState<Record<string, string>>({})
  // 离线皮肤库
  const [showSkinCatalog, setShowSkinCatalog] = useState(false)
  const [skinCatalog, setSkinCatalog] = useState<SkinChoice[]>([])

  const selected = items.find((item) => item.stableKey === selectedKey) ?? null
  const selectedIsDefault = !!selected && selected.isDefault
  const selectedDetail = selected ? accountDetail(selected.account) : ''

  const refresh = useCallback(async () => {
    try {
      const list = (await GetAccounts()) as LaunchAccount[]
      const selectedAccount = await GetSelectedAccount().catch(() => null)
      let selectedStable = ''
      if (selectedAccount) selectedStable = await GetAccountStableKey(selectedAccount as never)
      const withKeys = await Promise.all(list.map(async (account) => ({
        account,
        fallback: getFallbackText(account.DisplayName),
        isDefault: false,
        stableKey: await GetAccountStableKey(account as never),
        avatarSource: '',
      })))
      // 保持当前选中；未选中时回落到默认账号（列表首项）
      let nextSelected = selectedKey
      if (!withKeys.some((item) => item.stableKey === nextSelected)) {
        nextSelected = selectedStable || withKeys[0]?.stableKey || ''
      }
      const finalItems = withKeys.map((item) => ({
        ...item,
        isDefault: item.stableKey === (withKeys[0]?.stableKey ?? ''),
        avatarSource: '',
      }))
      setItems(finalItems)
      setSelectedKey(nextSelected)
      setStatusText(withKeys.length === 0
        ? '还没有账号，点击「＋ 添加账户」开始。'
        : `共 ${withKeys.length} 个账号，列表首项为默认账号。`)
      loadAvatars(withKeys)
    } catch (ex) {
      setStatusText(`读取账号列表失败：${(ex as Error)?.message ?? ex}`)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedKey])

  // 异步加载真实皮肤头像（GetAvatarUrl 失败时保留首字母占位）
  function loadAvatars(list: Array<{ stableKey: string }>) {
    list.forEach(({ stableKey }) => {
      GetAvatarUrl(stableKey)
        .then((uri) => { if (uri) setAvatarMap((m) => ({ ...m, [stableKey]: uri })) })
        .catch(() => { /* 保留首字母占位 */ })
    })
  }

  // 头像加载完成后并入列表
  useEffect(() => {
    setItems((prev) => {
      if (prev.length === 0) return prev
      let changed = false
      const next = prev.map((item) => {
        const uri = avatarMap[item.stableKey]
        if (uri && item.avatarSource !== uri) {
          changed = true
          return { ...item, avatarSource: uri }
        }
        return item
      })
      return changed ? next : prev
    })
  }, [avatarMap])

  function onAccountAdded(account: unknown) {
    const displayName = (account as { DisplayName?: string } | null)?.DisplayName
    setShowLogin(false)
    setStatusText(`已添加账号：${displayName ?? ''}`)
    refresh()
  }

  async function setDefault() {
    const item = selected
    if (!item) {
      setStatusText('请先选择一个账号。')
      return
    }
    try {
      await MoveAccountToTop(item.account as never)
      setStatusText(`已设为默认：${item.account.DisplayName}`)
      await refresh()
      setSelectedKey(item.stableKey)
    } catch (ex) {
      setStatusText(`设置默认账号失败：${(ex as Error)?.message ?? ex}`)
    }
  }

  async function removeAccount() {
    const item = selected
    if (!item) {
      setStatusText('请先选择一个账号。')
      return
    }
    if (!(await nyaConfirm('删除账户', `确定删除账号「${item.account.DisplayName}」？该操作不可恢复。`))) {
      return
    }
    try {
      await RemoveAccount(item.account as never)
      const remaining = items.length - 1
      setStatusText(remaining === 0
        ? `已删除账号：${item.account.DisplayName}（账号列表已清空）`
        : `已删除账号：${item.account.DisplayName}`)
      setSelectedKey('')
      await refresh()
    } catch (ex) {
      setStatusText(`删除账号失败：${(ex as Error)?.message ?? ex}`)
    }
  }

  async function changeSkin() {
    const item = selected
    if (!item) {
      setStatusText('请先选择一个账号。')
      return
    }
    const account = item.account
    if (account.Type === 'offline') {
      await changeOfflineSkinByFile(item)
      return
    }
    if (account.Type === 'authlib') {
      setStatusText('皮肤站账号请到对应皮肤站的网页端更换皮肤。')
      return
    }
    if (account.Type === 'microsoft') {
      await changeMicrosoftSkin(item)
      return
    }
    setStatusText('该账号暂不支持编辑皮肤。')
  }

  // 离线：选择本地 PNG 设为自定义皮肤（SetOfflineSkin 会复制到存储目录持久化）
  async function changeOfflineSkinByFile(item: AccountItem) {
    let path = ''
    try {
      path = await SelectFile('选择皮肤贴图（png）', '图片文件', '*.png')
    } catch { /* 用户取消 */ }
    if (!path) return
    try {
      await SetOfflineSkin(item.stableKey, path)
      setStatusText(`已设置离线自定义皮肤：${path.split(/[\\/]/).pop()}`)
      await refresh()
      setSelectedKey(item.stableKey)
      alert('离线自定义皮肤已应用。')
    } catch (ex) {
      setStatusText(`更换皮肤失败：${(ex as Error)?.message ?? ex}`)
      alert(String((ex as Error)?.message ?? ex))
    }
  }

  // 离线皮肤库：内置 9 款皮肤网格（对应 OfflineSkinPickerDialog）
  async function openSkinCatalog() {
    const item = selected
    if (!item || item.account.Type !== 'offline') {
      setStatusText('皮肤库仅支持离线账号。')
      return
    }
    try {
      setSkinCatalog(((await GetOfflineSkinCatalog()) ?? []) as SkinChoice[])
      setShowSkinCatalog(true)
    } catch (ex) {
      setStatusText(`读取皮肤库失败：${(ex as Error)?.message ?? ex}`)
    }
  }

  async function applyCatalogSkin(choice: SkinChoice) {
    const item = selected
    if (!item) return
    setShowSkinCatalog(false)
    try {
      await SetOfflineSkin(item.stableKey, choice.id)
      setStatusText(`已选择离线默认皮肤：${choice.displayName}`)
      await refresh()
      setSelectedKey(item.stableKey)
    } catch (ex) {
      setStatusText(`更换皮肤失败：${(ex as Error)?.message ?? ex}`)
    }
  }

  // 正版：选 png → 选模型（slim/classic）→ UploadSkin（对应 MinecraftAppearanceEditor）
  async function changeMicrosoftSkin(item: AccountItem) {
    let path = ''
    try {
      path = await SelectFile('选择 Minecraft Java 皮肤', 'Minecraft 皮肤 PNG', '*.png')
    } catch { /* 用户取消 */ }
    if (!path) return
    // C# SkinModelDialog 的等价简化：确认框选纤细，取消则经典
    const slim = await nyaConfirm('选择皮肤模型', '使用纤细（Alex）模型？\n取消则使用经典（Steve）模型。')
    const variant = slim ? 'slim' : 'classic'
    try {
      await UploadSkin(item.stableKey, path, variant)
      setStatusText('正版皮肤已更新。')
      await refresh()
      setSelectedKey(item.stableKey)
      alert('正版皮肤已更新，头像即将刷新（服务端可能有几分钟缓存延迟）。')
    } catch (ex) {
      setStatusText(`皮肤上传失败：${(ex as Error)?.message ?? ex}`)
      alert(String((ex as Error)?.message ?? ex))
    }
  }

  function changeCape() {
    // PORTING: C# 披风流程为 GetProfileAsync 读披风列表 → CapeSelectionDialog →
    // SetActiveCapeAsync（激活/停用已有披风；披风不支持上传）。绑定层暂未移植披风激活 API。
    setStatusText('披风不支持上传；披风切换（激活/停用已有披风）暂未开放，敬请期待。')
  }

  useEffect(() => { refresh() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  return (
    /* 账户管理页（AccountManagePage.axaml：标题区 / 主卡片 / 底部状态 三行布局） */
    <section className="grid h-full grid-rows-[auto_1fr_auto] px-10 pt-10 pb-8">
      {/* 标题区 */}
      <header className="mb-6">
        <h1 className="mb-2 text-[28px] font-bold leading-tight text-foreground">账户管理</h1>
        <p className="m-0 text-[13px] text-hint-text">管理正版与离线账号，切换默认账号，并编辑玩家外观</p>
      </header>

      {/* 主卡片：工具栏 + 账号列表 */}
      <Card className="grid min-h-0 grid-rows-[auto_1fr] rounded-2xl bg-secondary/60 p-5 px-6">
        {/* 工具栏 */}
        <div className="flex items-center gap-2.5">
          <div className="flex min-w-0 flex-1 flex-col gap-[3px]">
            <div className="flex min-w-0 items-center gap-2">
              <span className="overflow-hidden text-base font-semibold text-foreground text-ellipsis whitespace-nowrap" title={selected?.account.DisplayName}>
                {selected ? selected.account.DisplayName : '未选择账号'}
              </span>
              {selected && selectedIsDefault ? <Badge>默认账号</Badge> : null}
            </div>
            <span className="overflow-hidden text-xs text-hint-text text-ellipsis whitespace-nowrap" title={selectedDetail}>
              {selected ? selectedDetail : '请选择一个账号，再使用右侧工具按钮'}
            </span>
          </div>

          <Button title="添加正版（微软设备码登录）或离线账号" onClick={() => setShowLogin(true)}>
            <Icon name="account-plus" size={16} />
            <span>添加账户</span>
          </Button>

          {/* 操作菜单：默认/皮肤/披风 + 危险的删除操作 */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline">更多操作<ChevronDown size={14} className="text-subtext-text" /></Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-44">
              <DropdownMenuItem onSelect={setDefault}>设为默认</DropdownMenuItem>
              <DropdownMenuItem onSelect={changeSkin}>更换皮肤</DropdownMenuItem>
              <DropdownMenuItem disabled={selected?.account.Type !== 'offline'} onSelect={openSkinCatalog}>
                从皮肤库选择
              </DropdownMenuItem>
              <DropdownMenuItem disabled={selected?.account.Type !== 'microsoft'} onSelect={changeCape}>更换披风</DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem className="text-destructive focus:text-destructive" onSelect={removeAccount}>删除账户</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>

        {/* 账号列表 */}
        <div className="relative mt-4 min-h-0 overflow-y-auto">
          {items.map((item) => (
            <div
              key={item.stableKey}
              className={`mb-2.5 flex cursor-pointer items-center gap-3.5 rounded-xl bg-muted p-3 px-4 transition-shadow ${
                selectedKey === item.stableKey ? 'ring-1 ring-ring/70' : 'hover:bg-accent'
              }`}
              onClick={() => setSelectedKey(item.stableKey)}
            >
              {/* 头像：皮肤贴图加载失败时回退到字母占位 */}
              <div className="flex size-[54px] flex-none items-center justify-center overflow-hidden rounded-[10px] bg-badge">
                {item.avatarSource ? (
                  <img
                    src={item.avatarSource}
                    alt=""
                    className="size-full object-contain [image-rendering:pixelated]"
                    onError={() => {
                      setItems((prev) => prev.map((it) => (it.stableKey === item.stableKey ? { ...it, avatarSource: '' } : it)))
                    }} />
                ) : (
                  <span className="text-[22px] font-bold text-primary">{item.fallback}</span>
                )}
              </div>

              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <div className="flex min-w-0 items-center gap-2">
                  <span className="overflow-hidden text-[15px] font-semibold text-foreground text-ellipsis whitespace-nowrap" title={item.account.DisplayName}>
                    {item.account.DisplayName}
                  </span>
                  <Badge>{typeLabel(item.account)}</Badge>
                  {item.isDefault ? <Badge className="border-transparent bg-transparent text-success">默认</Badge> : null}
                </div>
                <span className="text-[11px] break-words text-hint-text">{accountDetail(item.account)}</span>
              </div>

              {item.isDefault ? <span className="flex-none text-[11px] font-semibold text-primary">当前</span> : null}
            </div>
          ))}

          {/* 空列表提示 */}
          {items.length === 0 ? (
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center">
              <span className="text-[40px] text-primary">☺</span>
              <span className="text-base font-semibold text-secondary-text">还没有任何账号</span>
              <span className="text-xs text-hint-text">点击上方「＋ 添加账户」新建账号，支持正版（微软设备码登录）与离线账号</span>
            </div>
          ) : null}
        </div>
      </Card>

      {/* 离线皮肤库选择浮层（对应 C# OfflineSkinPickerDialog：9 款内置皮肤网格） */}
      <Dialog open={showSkinCatalog} onOpenChange={(v) => { if (!v) setShowSkinCatalog(false) }}>
        <DialogContent className="max-w-[520px]">
          <DialogHeader>
            <DialogTitle>选择离线默认皮肤</DialogTitle>
            <DialogDescription>内置皮肤取自已安装客户端（无客户端时使用占位皮肤）</DialogDescription>
          </DialogHeader>
          <div className="grid max-h-[60vh] grid-cols-3 gap-2.5 overflow-y-auto pr-1">
            {skinCatalog.map((choice) => {
              const active = choice.id.toLowerCase() === (selected?.account.OfflineSkinId || 'steve').toLowerCase()
              return (
                <button
                  key={choice.id}
                  className={`flex cursor-pointer flex-col items-center gap-2 rounded-[9px] border border-medium-border bg-muted p-3.5 transition-colors hover:bg-accent ${
                    active ? 'border-accent ring-1 ring-ring/70' : ''
                  }`}
                  onClick={() => applyCatalogSkin(choice)}
                >
                  <div className="flex size-16 items-center justify-center overflow-hidden rounded-lg bg-badge">
                    {choice.source ? (
                      <img src={choice.source} alt="" className="size-full object-contain [image-rendering:pixelated]" />
                    ) : (
                      <span className="text-xl font-bold text-primary">{choice.fallbackText}</span>
                    )}
                  </div>
                  <span className="text-xs font-semibold text-secondary-text">
                    {choice.displayName}
                    {active ? <span className="ml-1 text-[10px] font-normal text-primary">当前使用</span> : null}
                  </span>
                  <span className="text-[10px] text-hint-text">
                    {choice.model === 'slim' ? '纤细模型 · Slim' : '经典模型 · Classic'}
                  </span>
                </button>
              )
            })}
          </div>
        </DialogContent>
      </Dialog>

      {/* 底部状态提示 */}
      <div className="mt-[18px] min-h-[1em] text-center text-xs break-words text-hint-text">{statusText}</div>

      {/* 新建账户 / 设备码 对话框（点击遮罩不关闭） */}
      <Dialog open={showLogin} onOpenChange={(v) => { if (!v) setShowLogin(false) }}>
        <DialogContent
          className="max-w-[460px] gap-0 border-0 bg-transparent p-0 shadow-none"
          showCloseButton={false}
          onEscapeKeyDown={(e) => e.preventDefault()}
          onPointerDownOutside={(e) => e.preventDefault()}
          onInteractOutside={(e) => e.preventDefault()}
        >
          <AccountLoginOverlay onClose={() => setShowLogin(false)} onAdded={onAccountAdded} />
        </DialogContent>
      </Dialog>
    </section>
  )
}
