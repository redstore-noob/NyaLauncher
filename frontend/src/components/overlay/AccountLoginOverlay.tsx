/*
 * 可复用"新建账户"浮层（等价 Vue 版 components/overlay/AccountLoginOverlay.vue）：
 * 正版（微软设备码）/ 离线 / 皮肤站（authlib-injector）三种入口。
 * 设备码经 EventsOn('auth:deviceCode') 接收（自动复制剪贴板 + 打开浏览器），
 * 取消调 CancelMicrosoftLogin。添加成功（已持久化）后回调 onAdded(account)；
 * 请求关闭时回调 onClose()。
 */
import { useEffect, useRef, useState } from 'react'
import Icon from './Icon'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  GetAccounts, HasOfflineName, CreateOfflineAccount,
  LoginMicrosoft, CancelMicrosoftLogin, AddAccount, UpdateMicrosoftAccount, MoveAccountToTop,
  ResolveAuthlibServer, AuthlibLogin, UpdateAuthlibAccount,
} from '../../../wailsjs/go/bindings/AccountAPI.js'
import { EventsOn, EventsOff, BrowserOpenURL, ClipboardSetText } from '../../../wailsjs/runtime/runtime.js'

interface DeviceCodeInfo { UserCode?: string; VerificationUri?: string }

export interface AccountLoginOverlayProps {
  onClose: () => void
  onAdded: (account: unknown) => void
}

type ViewKind = 'main' | 'external' | 'deviceCode'

export default function AccountLoginOverlay({ onClose, onAdded }: AccountLoginOverlayProps) {
  const [view, setView] = useState<ViewKind>('main')
  const [offlineVisible, setOfflineVisible] = useState(false)
  const [offlineName, setOfflineName] = useState('Player_01')
  const [hintText, setHintText] = useState('')

  // 皮肤站
  const [externalServer, setExternalServer] = useState('')
  const [externalUsername, setExternalUsername] = useState('')
  const [externalPassword, setExternalPassword] = useState('')
  const [externalProfiles, setExternalProfiles] = useState<Array<{ Id: string; Name: string }>>([])
  const [selectedProfileIdx, setSelectedProfileIdx] = useState('')
  const [externalStatus, setExternalStatus] = useState('')
  const [externalBusy, setExternalBusy] = useState(false)
  const pendingExternal = useRef<{ server: AuthlibServer; login: AuthlibLoginResult; username: string } | null>(null)

  // 设备码
  const [deviceCode, setDeviceCode] = useState('')
  const [deviceCodeUrl, setDeviceCodeUrl] = useState('')
  const [deviceCodeHint, setDeviceCodeHint] = useState('请在浏览器中打开以下地址，然后输入验证码')
  const [deviceCodeStatus, setDeviceCodeStatus] = useState('')
  const deviceCodeActive = useRef(false)

  interface AuthlibServer { ApiRoot: string; ServerName?: string }
  interface AuthlibLoginResult { AccessToken: string; Profiles: Array<{ Id: string; Name: string }> }

  function close() {
    cancelDeviceCodePolling()
    onClose()
  }

  function backToMain() {
    cancelDeviceCodePolling()
    setExternalBusy(false)
    setView('main')
    setOfflineVisible(false)
    setHintText('')
    setDeviceCodeStatus('')
    setOfflineName('Player_01')
    resetExternalView()
  }

  function resetExternalView() {
    pendingExternal.current = null
    setExternalProfiles([])
    setSelectedProfileIdx('')
    setExternalStatus('')
    setExternalPassword('')
  }

  // ------------------------------------------------------------------
  // 离线账号
  // ------------------------------------------------------------------

  function showOfflinePanel() {
    setHintText('')
    setOfflineVisible(true)
  }

  async function confirmOffline() {
    const name = offlineName.trim()
    if (!name) {
      setHintText('请输入离线用户名。')
      return
    }
    try {
      if (await HasOfflineName(name)) {
        setHintText(`已存在同名离线账号：${name}`)
        return
      }
      const account = await CreateOfflineAccount(name)
      onClose()
      onAdded(account)
    } catch (ex) {
      setHintText(`创建离线账号失败：${(ex as Error)?.message ?? ex}`)
    }
  }

  // ------------------------------------------------------------------
  // 微软设备码登录
  // ------------------------------------------------------------------

  function onDeviceCodeEvent(info: DeviceCodeInfo) {
    setDeviceCodeHint('验证码已自动复制到剪贴板，在浏览器中打开下方地址并粘贴即可~')
    setDeviceCode(info?.UserCode ?? '')
    setDeviceCodeUrl(info?.VerificationUri ?? '')
    setDeviceCodeStatus('')
    ClipboardSetText(info?.UserCode ?? '').catch(() => { /* ignore */ })
    // 自动打开浏览器（失败时用户仍可点「打开浏览器」）
    if (info?.VerificationUri) openBrowser(info?.UserCode ?? '')
  }

  function openBrowser(code?: string) {
    const c = code ?? deviceCode
    if (!c) return
    BrowserOpenURL(`https://www.microsoft.com/link?user_code=${c}`)
  }

  function startMicrosoftLogin() {
    setView('deviceCode')
    setDeviceCode('')
    setDeviceCodeUrl('')
    setDeviceCodeHint('请在浏览器中打开以下地址，然后输入验证码')
    deviceCodeActive.current = true
    EventsOn('auth:deviceCode', onDeviceCodeEvent)

    LoginMicrosoft()
      .then(async (msAccount) => {
        stopDeviceCodePolling()
        // 同一微软账号（按档案 UUID）已存在则更新凭据并置顶（视为重新登录）
        const accounts = await GetAccounts()
        const existing = accounts.find((a) =>
          a.Type === 'microsoft' && a.Microsoft?.Uuid && a.Microsoft.Uuid === msAccount.Uuid)
        let entry
        if (existing) {
          await UpdateMicrosoftAccount(existing, msAccount)
          await MoveAccountToTop(existing)
          entry = existing
        } else {
          entry = {
            Type: 'microsoft',
            DisplayName: msAccount.Username,
            OfflineName: '',
            OfflineSkinId: '',
            Microsoft: msAccount,
          }
          await AddAccount(entry as never)
        }
        onClose()
        onAdded(entry)
      })
      .catch((ex) => {
        const cancelled = !deviceCodeActive.current // 用户已点「取消登录」时后端会中止轮询并 reject
        stopDeviceCodePolling()
        backToMain()
        if (!cancelled) setHintText(`微软账号登录失败：${(ex as Error)?.message ?? ex}`)
      })
  }

  async function cancelDeviceCode() {
    stopDeviceCodePolling()
    setDeviceCodeStatus('正在取消登录…')
    try {
      await CancelMicrosoftLogin()
    } catch { /* 后端已结束轮询时忽略 */ }
    setDeviceCodeStatus('')
    backToMain()
  }

  function stopDeviceCodePolling() {
    deviceCodeActive.current = false
    EventsOff('auth:deviceCode')
  }

  function cancelDeviceCodePolling() {
    if (deviceCodeActive.current) stopDeviceCodePolling()
  }

  // 卸载时兜底清理订阅
  useEffect(() => () => {
    if (deviceCodeActive.current) EventsOff('auth:deviceCode')
  }, [])

  // ------------------------------------------------------------------
  // 皮肤站（authlib-injector）
  // ------------------------------------------------------------------

  async function externalLogin() {
    const serverText = externalServer.trim()
    const username = externalUsername.trim()
    const password = externalPassword
    if (!serverText) {
      setExternalStatus('请输入皮肤站地址。')
      return
    }
    if (!username || !password) {
      setExternalStatus('请输入皮肤站账号与密码。')
      return
    }

    setExternalBusy(true)
    try {
      setExternalStatus('正在解析皮肤站…')
      const server = (await ResolveAuthlibServer(serverText)) as AuthlibServer
      setExternalStatus(`正在登录 ${server.ServerName || '皮肤站'}…`)
      const login = (await AuthlibLogin(server.ApiRoot, username, password, '')) as AuthlibLoginResult

      if (!login.Profiles || login.Profiles.length === 0) {
        setExternalStatus('该账号在此皮肤站没有角色档案，请先在皮肤站创建角色。')
        return
      }
      if (login.Profiles.length === 1) {
        addExternalAccount(server, login, login.Profiles[0], username)
        return
      }
      // 多角色：等待用户选择后确认添加（radix Select 只支持字符串 value，存索引）
      pendingExternal.current = { server, login, username }
      setExternalProfiles(login.Profiles)
      setSelectedProfileIdx('0')
      setExternalStatus(`该账号有 ${login.Profiles.length} 个角色，请选择要添加的角色。`)
    } catch (ex) {
      setExternalStatus(`登录失败：${(ex as Error)?.message ?? ex}`)
    } finally {
      setExternalBusy(false)
    }
  }

  function confirmExternalProfile() {
    const pending = pendingExternal.current
    if (!pending) return
    const idx = Number(selectedProfileIdx)
    const profile = pending.login.Profiles[Number.isInteger(idx) ? idx : -1]
    if (!profile) {
      setExternalStatus('请选择一个角色。')
      return
    }
    addExternalAccount(pending.server, pending.login, profile, pending.username)
  }

  async function addExternalAccount(
    server: AuthlibServer, login: AuthlibLoginResult,
    profile: { Id: string; Name: string }, username: string,
  ) {
    const credential = {
      Username: username,
      ProfileName: profile.Name,
      ProfileUuid: profile.Id,
      AccessToken: login.AccessToken,
      ApiRoot: server.ApiRoot,
      ServerName: server.ServerName ?? '',
    }

    // 皮肤站账号身份 = 角色 UUID + API 根：同一 UUID 在不同皮肤站是不同账号
    const accounts = await GetAccounts()
    const existing = accounts.find((a) =>
      a.Type === 'authlib' && a.Authlib &&
      (a.Authlib.ProfileUuid || '').toLowerCase() === (profile.Id || '').toLowerCase() &&
      (a.Authlib.ApiRoot || '').replace(/\/+$/, '').toLowerCase() ===
        (server.ApiRoot || '').replace(/\/+$/, '').toLowerCase())

    let entry
    if (existing) {
      await UpdateAuthlibAccount(existing, credential)
      await MoveAccountToTop(existing)
      entry = existing
    } else {
      entry = {
        Type: 'authlib',
        DisplayName: profile.Name,
        OfflineName: '',
        OfflineSkinId: '',
        Authlib: credential,
      }
      await AddAccount(entry as never)
    }
    onClose()
    onAdded(entry)
  }

  // ------------------------------------------------------------------
  // 渲染
  // ------------------------------------------------------------------

  return (
    /* 新建账户浮层卡片（AccountLoginOverlay.axaml：380 宽 / PanelBg / r16 / padding 28,24） */
    <div className="flex w-[380px] max-w-[calc(100vw-48px)] flex-col rounded-2xl border border-input bg-card p-6 shadow-xl">

      {/* 主视图：类型选择 + 离线输入 */}
      {view === 'main' ? (
        <div className="flex flex-col gap-4">
          <div className="text-center text-xl font-bold text-foreground">新建账户</div>

          <div className="grid grid-cols-3 gap-2">
            <Button variant="secondary" className="h-[52px] rounded-[10px] text-[13px] font-semibold" onClick={startMicrosoftLogin}>
              <Icon name="account-circle" size={16} />
              <span>正版账号</span>
            </Button>
            <Button variant="secondary" className="h-[52px] rounded-[10px] text-[13px] font-semibold" onClick={showOfflinePanel}>离线账号</Button>
            <Button variant="secondary" className="h-[52px] rounded-[10px] text-[13px] font-semibold" onClick={() => setView('external')}>皮肤站账号</Button>
          </div>

          {/* 离线账号输入区 */}
          {offlineVisible ? (
            <div className="flex flex-col gap-2.5">
              <Input
                value={offlineName}
                maxLength={16}
                placeholder="输入离线用户名"
                onChange={(e) => setOfflineName(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') confirmOffline() }} />
              <Button onClick={confirmOffline}>确认添加</Button>
            </div>
          ) : null}

          {hintText ? <div className="text-[11px] break-words text-hint-text select-text">{hintText}</div> : null}

          <Button variant="ghost" className="h-[34px] text-xs text-hint-text hover:text-foreground" onClick={close}>取消</Button>
        </div>
      ) : null}

      {/* 皮肤站（外置登录）视图 */}
      {view === 'external' ? (
        <div className="flex flex-col gap-3">
          <div className="text-center text-xl font-bold text-foreground">皮肤站登录</div>
          <div className="text-center text-[11px] text-hint-text">使用 authlib-injector 外置登录（Yggdrasil 规范皮肤站）</div>

          <div className="flex flex-col gap-1">
            <label className="text-xs text-secondary-text">皮肤站地址</label>
            <div className="flex gap-2">
              <Input value={externalServer} placeholder="如 littleskin.cn" className="min-w-0 flex-1"
                onChange={(e) => setExternalServer(e.target.value)} />
              <Button variant="secondary" size="sm" className="h-auto rounded-lg px-2.5 py-1.5 text-xs"
                onClick={() => setExternalServer('littleskin.cn')}>LittleSkin</Button>
            </div>
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-xs text-secondary-text">账号</label>
            <Input value={externalUsername} placeholder="皮肤站邮箱或用户名" onChange={(e) => setExternalUsername(e.target.value)} />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs text-secondary-text">密码</label>
            <Input type="password" value={externalPassword} placeholder="皮肤站密码" onChange={(e) => setExternalPassword(e.target.value)} />
          </div>

          {externalProfiles.length > 0 ? (
            <div className="flex flex-col gap-1">
              <label className="text-xs text-secondary-text">选择角色</label>
              <Select value={selectedProfileIdx || undefined} onValueChange={setSelectedProfileIdx}>
                <SelectTrigger className="w-full"><SelectValue placeholder="选择角色" /></SelectTrigger>
                <SelectContent>
                  {externalProfiles.map((p, i) => (
                    <SelectItem key={p.Id} value={String(i)}>{p.Name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          ) : null}

          {externalProfiles.length === 0
            ? <Button disabled={externalBusy} onClick={externalLogin}>登录</Button>
            : <Button onClick={confirmExternalProfile}>确认添加</Button>}

          {externalStatus ? <div className="text-center text-[11px] text-hint-text">{externalStatus}</div> : null}

          <Button variant="ghost" className="h-[34px] text-xs text-hint-text hover:text-foreground" onClick={backToMain}>返回</Button>
        </div>
      ) : null}

      {/* 设备码视图 */}
      {view === 'deviceCode' ? (
        <div className="flex flex-col gap-3.5">
          <div className="text-center text-xl font-bold text-foreground">微软账号登录</div>
          <div className="text-center text-[11px] text-hint-text">{deviceCodeHint}</div>
          <div className="self-center rounded-[10px] bg-secondary px-7 py-3">
            <span className="select-text text-3xl font-bold tracking-[8px] text-primary">{deviceCode || '·····'}</span>
          </div>
          <div className="text-center text-[11px] text-hint-text select-text">{deviceCodeUrl}</div>
          {deviceCodeStatus ? <div className="text-center text-[11px] text-hint-text">{deviceCodeStatus}</div> : null}
          <div className="flex justify-center gap-2.5">
            <Button variant="secondary" size="sm" className="h-auto rounded-lg px-3.5 py-[7px]" onClick={() => openBrowser()}>打开浏览器</Button>
            <Button variant="secondary" size="sm" className="h-auto rounded-lg px-3.5 py-[7px]" onClick={backToMain}>返回</Button>
            <Button variant="ghost" size="sm" className="h-auto rounded-lg px-3.5 py-[7px] text-hint-text" onClick={cancelDeviceCode}>取消登录</Button>
          </div>
        </div>
      ) : null}

    </div>
  )
}
