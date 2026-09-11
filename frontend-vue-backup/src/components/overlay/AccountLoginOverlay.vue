<template>
  <!-- 新建账户浮层卡片（AccountLoginOverlay.axaml：380 宽 / PanelBg / r16 / padding 28,24） -->
  <div class="flex w-[380px] max-w-[calc(100vw-48px)] flex-col rounded-2xl border border-input bg-card p-6 shadow-xl">

    <!-- 主视图：类型选择 + 离线输入 -->
    <div v-if="view === 'main'" class="flex flex-col gap-4">
      <div class="text-center text-xl font-bold text-foreground">新建账户</div>

      <div class="grid grid-cols-3 gap-2">
        <Button variant="secondary" class="h-[52px] rounded-[10px] text-[13px] font-semibold" @click="startMicrosoftLogin">
          <Icon name="account-circle" :size="16" />
          <span>正版账号</span>
        </Button>
        <Button variant="secondary" class="h-[52px] rounded-[10px] text-[13px] font-semibold" @click="showOfflinePanel">离线账号</Button>
        <Button variant="secondary" class="h-[52px] rounded-[10px] text-[13px] font-semibold" @click="showExternalView">皮肤站账号</Button>
      </div>

      <!-- 离线账号输入区 -->
      <div v-if="offlineVisible" class="flex flex-col gap-2.5">
        <Input v-model="offlineName" maxlength="16" placeholder="输入离线用户名" @keydown.enter="confirmOffline" />
        <Button @click="confirmOffline">确认添加</Button>
      </div>

      <div v-if="hintText" class="text-[11px] text-hint-text break-words select-text">{{ hintText }}</div>

      <Button variant="ghost" class="h-[34px] text-xs text-hint-text hover:text-foreground" @click="close">取消</Button>
    </div>

    <!-- 皮肤站（外置登录）视图 -->
    <div v-else-if="view === 'external'" class="flex flex-col gap-3">
      <div class="text-center text-xl font-bold text-foreground">皮肤站登录</div>
      <div class="text-center text-[11px] text-hint-text">使用 authlib-injector 外置登录（Yggdrasil 规范皮肤站）</div>

      <div class="flex flex-col gap-1">
        <label class="text-xs text-secondary-text">皮肤站地址</label>
        <div class="flex gap-2">
          <Input v-model="externalServer" placeholder="如 littleskin.cn" class="min-w-0 flex-1" />
          <Button variant="secondary" size="sm" class="h-auto rounded-lg px-2.5 py-1.5 text-xs" @click="externalServer = 'littleskin.cn'">LittleSkin</Button>
        </div>
      </div>

      <div class="flex flex-col gap-1">
        <label class="text-xs text-secondary-text">账号</label>
        <Input v-model="externalUsername" placeholder="皮肤站邮箱或用户名" />
      </div>
      <div class="flex flex-col gap-1">
        <label class="text-xs text-secondary-text">密码</label>
        <Input v-model="externalPassword" type="password" placeholder="皮肤站密码" />
      </div>

      <div v-if="externalProfiles.length > 0" class="flex flex-col gap-1">
        <label class="text-xs text-secondary-text">选择角色</label>
        <Select v-model="selectedProfile">
          <SelectTrigger><SelectValue placeholder="选择角色" /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="p in externalProfiles" :key="p.Id" :value="p">{{ p.Name }}</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <Button v-if="externalProfiles.length === 0" :disabled="externalBusy" @click="externalLogin">登录</Button>
      <Button v-else @click="confirmExternalProfile">确认添加</Button>

      <div v-if="externalStatus" class="text-center text-[11px] text-hint-text">{{ externalStatus }}</div>

      <Button variant="ghost" class="h-[34px] text-xs text-hint-text hover:text-foreground" @click="backToMain">返回</Button>
    </div>

    <!-- 设备码视图 -->
    <div v-else-if="view === 'deviceCode'" class="flex flex-col gap-3.5">
      <div class="text-center text-xl font-bold text-foreground">微软账号登录</div>
      <div class="text-center text-[11px] text-hint-text">{{ deviceCodeHint }}</div>
      <div class="self-center rounded-[10px] bg-secondary px-7 py-3">
        <span class="select-text text-3xl font-bold tracking-[8px] text-primary">{{ deviceCode || '·····' }}</span>
      </div>
      <div class="text-center text-[11px] text-hint-text select-text">{{ deviceCodeUrl }}</div>
      <div v-if="deviceCodeStatus" class="text-center text-[11px] text-hint-text">{{ deviceCodeStatus }}</div>
      <div class="flex justify-center gap-2.5">
        <Button variant="secondary" size="sm" class="h-auto rounded-lg px-3.5 py-[7px]" @click="openBrowser">打开浏览器</Button>
        <Button variant="secondary" size="sm" class="h-auto rounded-lg px-3.5 py-[7px]" @click="backToMain">返回</Button>
        <Button variant="ghost" size="sm" class="h-auto rounded-lg px-3.5 py-[7px] text-hint-text" @click="cancelDeviceCode">取消登录</Button>
      </div>
    </div>

  </div>
</template>

<script setup>
/*
 * 可复用"新建账户"浮层（AccountLoginOverlay.axaml + .cs 的移植）：
 * 正版（微软设备码）/ 离线 / 皮肤站（authlib-injector）三种入口。
 * 添加成功（已持久化）后 emit('added', account)；请求关闭时 emit('close')。
 */
import { ref } from 'vue'
import Icon from './Icon.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  GetAccounts, HasOfflineName, CreateOfflineAccount,
  LoginMicrosoft, CancelMicrosoftLogin, AddAccount, UpdateMicrosoftAccount, MoveAccountToTop,
  ResolveAuthlibServer, AuthlibLogin, UpdateAuthlibAccount,
} from '../../../wailsjs/go/bindings/AccountAPI.js'
import { EventsOn, EventsOff, BrowserOpenURL, ClipboardSetText } from '../../../wailsjs/runtime/runtime.js'

const emit = defineEmits(['close', 'added'])

const view = ref('main')
const offlineVisible = ref(false)
const offlineName = ref('Player_01')
const hintText = ref('')

// 皮肤站
const externalServer = ref('')
const externalUsername = ref('')
const externalPassword = ref('')
const externalProfiles = ref([])
const selectedProfile = ref(null)
const externalStatus = ref('')
const externalBusy = ref(false)
let pendingExternal = null // { server, login, username }

// 设备码
const deviceCode = ref('')
const deviceCodeUrl = ref('')
const deviceCodeHint = ref('请在浏览器中打开以下地址，然后输入验证码')
const deviceCodeStatus = ref('')
const deviceCodeActive = ref(false)

function close() {
  cancelDeviceCodePolling()
  emit('close')
}

function backToMain() {
  cancelDeviceCodePolling()
  externalBusy.value = false
  view.value = 'main'
  offlineVisible.value = false
  hintText.value = ''
  deviceCodeStatus.value = ''
  offlineName.value = 'Player_01'
  resetExternalView()
}

function resetExternalView() {
  pendingExternal = null
  externalProfiles.value = []
  selectedProfile.value = null
  externalStatus.value = ''
  externalPassword.value = ''
}

// ------------------------------------------------------------------
// 离线账号
// ------------------------------------------------------------------

async function showOfflinePanel() {
  hintText.value = ''
  offlineVisible.value = true
}

async function confirmOffline() {
  const name = offlineName.value.trim()
  if (!name) {
    hintText.value = '请输入离线用户名。'
    return
  }
  try {
    if (await HasOfflineName(name)) {
      hintText.value = `已存在同名离线账号：${name}`
      return
    }
    const account = await CreateOfflineAccount(name)
    emit('close')
    emit('added', account)
  } catch (ex) {
    hintText.value = `创建离线账号失败：${ex?.message ?? ex}`
  }
}

// ------------------------------------------------------------------
// 微软设备码登录
// ------------------------------------------------------------------

function onDeviceCodeEvent(info) {
  deviceCodeHint.value = '验证码已自动复制到剪贴板，在浏览器中打开下方地址并粘贴即可~'
  deviceCode.value = info?.UserCode ?? ''
  deviceCodeUrl.value = info?.VerificationUri ?? ''
  deviceCodeStatus.value = ''
  ClipboardSetText(info?.UserCode ?? '').catch(() => {})
  // 自动打开浏览器（失败时用户仍可点"打开浏览器"）
  if (info?.VerificationUri) {
    openBrowser()
  }
}

function openBrowser() {
  if (!deviceCode.value) return
  BrowserOpenURL(`https://www.microsoft.com/link?user_code=${deviceCode.value}`)
}

function startMicrosoftLogin() {
  view.value = 'deviceCode'
  deviceCode.value = ''
  deviceCodeUrl.value = ''
  deviceCodeHint.value = '请在浏览器中打开以下地址，然后输入验证码'
  deviceCodeActive.value = true
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
        await AddAccount(entry)
      }
      emit('close')
      emit('added', entry)
    })
    .catch((ex) => {
      const cancelled = !deviceCodeActive.value // 用户已点「取消登录」时后端会中止轮询并 reject
      stopDeviceCodePolling()
      backToMain()
      if (!cancelled) hintText.value = `微软账号登录失败：${ex?.message ?? ex}`
    })
}

async function cancelDeviceCode() {
  stopDeviceCodePolling()
  deviceCodeStatus.value = '正在取消登录…'
  try {
    await CancelMicrosoftLogin()
  } catch { /* 后端已结束轮询时忽略 */ }
  deviceCodeStatus.value = ''
  backToMain()
}

function stopDeviceCodePolling() {
  deviceCodeActive.value = false
  EventsOff('auth:deviceCode')
}

function cancelDeviceCodePolling() {
  if (deviceCodeActive.value) stopDeviceCodePolling()
}

// ------------------------------------------------------------------
// 皮肤站（authlib-injector）
// ------------------------------------------------------------------

async function externalLogin() {
  const serverText = externalServer.value.trim()
  const username = externalUsername.value.trim()
  const password = externalPassword.value
  if (!serverText) {
    externalStatus.value = '请输入皮肤站地址。'
    return
  }
  if (!username || !password) {
    externalStatus.value = '请输入皮肤站账号与密码。'
    return
  }

  externalBusy.value = true
  try {
    externalStatus.value = '正在解析皮肤站…'
    const server = await ResolveAuthlibServer(serverText)
    externalStatus.value = `正在登录 ${server.ServerName || '皮肤站'}…`
    const login = await AuthlibLogin(server.ApiRoot, username, password, '')

    if (!login.Profiles || login.Profiles.length === 0) {
      externalStatus.value = '该账号在此皮肤站没有角色档案，请先在皮肤站创建角色。'
      return
    }
    if (login.Profiles.length === 1) {
      addExternalAccount(server, login, login.Profiles[0], username)
      return
    }
    // 多角色：等待用户选择后确认添加
    pendingExternal = { server, login, username }
    externalProfiles.value = login.Profiles
    selectedProfile.value = login.Profiles[0]
    externalStatus.value = `该账号有 ${login.Profiles.length} 个角色，请选择要添加的角色。`
  } catch (ex) {
    externalStatus.value = `登录失败：${ex?.message ?? ex}`
  } finally {
    externalBusy.value = false
  }
}

function confirmExternalProfile() {
  if (!pendingExternal) return
  if (!selectedProfile.value) {
    externalStatus.value = '请选择一个角色。'
    return
  }
  addExternalAccount(pendingExternal.server, pendingExternal.login, selectedProfile.value, pendingExternal.username)
}

async function addExternalAccount(server, login, profile, username) {
  const credential = {
    Username: username,
    ProfileName: profile.Name,
    ProfileUuid: profile.Id,
    AccessToken: login.AccessToken,
    ApiRoot: server.ApiRoot,
    ServerName: server.ServerName,
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
    await AddAccount(entry)
  }
  emit('close')
  emit('added', entry)
}
</script>
