<template>
  <!-- 账户管理页（AccountManagePage.axaml：标题区 / 主卡片 / 底部状态 三行布局） -->
  <section class="grid h-full grid-rows-[auto_1fr_auto] px-10 pt-10 pb-8">

    <!-- 标题区 -->
    <header class="mb-6">
      <h1 class="mb-2 text-[28px] font-bold leading-tight text-foreground">账户管理</h1>
      <p class="m-0 text-[13px] text-hint-text">管理正版与离线账号，切换默认账号，并编辑玩家外观</p>
    </header>

    <!-- 主卡片：工具栏 + 账号列表 -->
    <Card class="grid min-h-0 grid-rows-[auto_1fr] rounded-2xl bg-secondary/60 p-5 px-6">

      <!-- 工具栏 -->
      <div class="flex items-center gap-2.5">
        <div class="flex min-w-0 flex-1 flex-col gap-[3px]">
          <div class="flex min-w-0 items-center gap-2">
            <span class="overflow-hidden text-base font-semibold text-foreground text-ellipsis whitespace-nowrap" :title="selected?.DisplayName">
              {{ selected ? selected.DisplayName : '未选择账号' }}
            </span>
            <Badge v-if="selected && selectedIsDefault">默认账号</Badge>
          </div>
          <span class="overflow-hidden text-xs text-hint-text text-ellipsis whitespace-nowrap" :title="selectedDetail">
            {{ selected ? selectedDetail : '请选择一个账号，再使用右侧工具按钮' }}
          </span>
        </div>

        <Button title="添加正版（微软设备码登录）或离线账号" @click="showLogin = true">
          <Icon name="account-plus" :size="16" />
          <span>添加账户</span>
        </Button>

        <!-- 操作菜单：默认/皮肤/披风 + 危险的删除操作 -->
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button variant="outline">更多操作<ChevronDown :size="14" class="text-subtext-text" /></Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" class="w-44">
            <DropdownMenuItem @select="setDefault">设为默认</DropdownMenuItem>
            <DropdownMenuItem @select="changeSkin">更换皮肤</DropdownMenuItem>
            <DropdownMenuItem :disabled="selected?.account.Type !== 'offline'" @select="openSkinCatalog">
              从皮肤库选择
            </DropdownMenuItem>
            <DropdownMenuItem :disabled="selected?.account.Type !== 'microsoft'" @select="changeCape">更换披风</DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem class="text-destructive focus:text-destructive" @select="removeAccount">删除账户</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <!-- 账号列表 -->
      <div class="relative mt-4 min-h-0 overflow-y-auto">
        <div
          v-for="item in items"
          :key="item.stableKey"
          class="mb-2.5 flex cursor-pointer items-center gap-3.5 rounded-xl bg-muted p-3 px-4 transition-shadow"
          :class="selectedKey === item.stableKey ? 'ring-1 ring-ring/70' : 'hover:bg-accent'"
          @click="selected = item"
        >
          <!-- 头像：皮肤贴图加载失败时回退到字母占位 -->
          <div class="flex size-[54px] flex-none items-center justify-center overflow-hidden rounded-[10px] bg-badge">
            <img v-if="item.avatarSource" :src="item.avatarSource" alt="" class="size-full object-contain [image-rendering:pixelated]" @error="item.avatarSource = ''" />
            <span v-else class="text-[22px] font-bold text-primary">{{ item.fallback }}</span>
          </div>

          <div class="flex min-w-0 flex-1 flex-col gap-1">
            <div class="flex min-w-0 items-center gap-2">
              <span class="overflow-hidden text-[15px] font-semibold text-foreground text-ellipsis whitespace-nowrap" :title="item.account.DisplayName">{{ item.account.DisplayName }}</span>
              <Badge>{{ typeLabel(item.account) }}</Badge>
              <Badge v-if="item.isDefault" class="border-transparent bg-transparent text-success">默认</Badge>
            </div>
            <span class="text-[11px] text-hint-text break-words">{{ accountDetail(item.account) }}</span>
          </div>

          <span v-if="item.isDefault" class="flex-none text-[11px] font-semibold text-primary">当前</span>
        </div>

        <!-- 空列表提示 -->
        <div v-if="items.length === 0" class="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center">
          <span class="text-[40px] text-primary">☺</span>
          <span class="text-base font-semibold text-secondary-text">还没有任何账号</span>
          <span class="text-xs text-hint-text">点击上方「＋ 添加账户」新建账号，支持正版（微软设备码登录）与离线账号</span>
        </div>
      </div>
    </Card>

    <!-- 离线皮肤库选择浮层（对应 C# OfflineSkinPickerDialog：9 款内置皮肤网格） -->
    <Dialog :open="showSkinCatalog" @update:open="(v) => { if (!v) showSkinCatalog = false }">
      <DialogContent class="max-w-[520px]">
        <DialogHeader>
          <DialogTitle>选择离线默认皮肤</DialogTitle>
          <DialogDescription>内置皮肤取自已安装客户端（无客户端时使用占位皮肤）</DialogDescription>
        </DialogHeader>
        <div class="grid max-h-[60vh] grid-cols-3 gap-2.5 overflow-y-auto pr-1">
          <button
            v-for="choice in skinCatalog"
            :key="choice.id"
            class="flex cursor-pointer flex-col items-center gap-2 rounded-[9px] border border-medium-border bg-muted p-3.5 transition-colors hover:bg-accent"
            :class="choice.id.toLowerCase() === (selected?.account.OfflineSkinId || 'steve').toLowerCase()
              ? 'border-accent ring-1 ring-ring/70' : ''"
            @click="applyCatalogSkin(choice)"
          >
            <div class="flex size-16 items-center justify-center overflow-hidden rounded-lg bg-badge">
              <img
                v-if="choice.source"
                :src="choice.source"
                alt=""
                class="size-full object-contain [image-rendering:pixelated]"
              />
              <span v-else class="text-xl font-bold text-primary">{{ choice.fallbackText }}</span>
            </div>
            <span class="text-xs font-semibold text-secondary-text">
              {{ choice.displayName }}
              <span v-if="choice.id.toLowerCase() === (selected?.account.OfflineSkinId || 'steve').toLowerCase()"
                class="ml-1 text-[10px] font-normal text-primary">当前使用</span>
            </span>
            <span class="text-[10px] text-hint-text">
              {{ choice.model === 'slim' ? '纤细模型 · Slim' : '经典模型 · Classic' }}
            </span>
          </button>
        </div>
      </DialogContent>
    </Dialog>

    <!-- 底部状态提示 -->
    <div class="mt-[18px] min-h-[1em] text-center text-xs text-hint-text break-words">{{ statusText }}</div>

    <!-- 新建账户 / 设备码 对话框（Dialog 重构，点击遮罩不关闭） -->
    <Dialog :open="showLogin" @update:open="(v) => { if (!v) showLogin = false }">
      <DialogContent
        class="max-w-[460px] gap-0 border-0 bg-transparent p-0 shadow-none"
        :show-close-button="false"
        @escape-key-down.prevent
        @pointer-down-outside.prevent
        @interact-outside.prevent
      >
        <AccountLoginOverlay @close="showLogin = false" @added="onAccountAdded" />
      </DialogContent>
    </Dialog>
  </section>
</template>

<script setup>
/*
 * 账户管理页（AccountManagePage.axaml + .cs 移植）。
 * 列表/默认账号与 AccountStore（AccountAPI）双向同步；添加账号复用 AccountLoginOverlay。
 * 头像经 GetAvatarUrl（SkinAPI 扩展：正版 Mojang 档案 / authlib sessionserver /
 * 离线皮肤目录解析），失败回退首字母。
 * 更换皮肤：正版 SelectFile → UploadSkin（multipart，对应 MinecraftProfileService）； 
 * 离线 SelectFile → SetOfflineSkin，或经「从皮肤库选择」打开内置皮肤网格
 * （对应 OfflineSkinPickerDialog，数据源 GetOfflineSkinCatalog）。
 * PORTING: 披风仍不可用——C# 有 SetActiveCapeAsync（披风激活/停用，非上传），
 * 绑定层暂未移植，保留文件选择现状并提示暂未开放。
 */
import { computed, onMounted, ref } from 'vue'
import { ChevronDown } from '@lucide/vue'
import Icon from '../../components/overlay/Icon.vue'
import AccountLoginOverlay from '../../components/overlay/AccountLoginOverlay.vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { alert, confirm as nyaConfirm } from '../../composables/dialog.js'
import { SelectFile } from '../../../wailsjs/go/bindings/SystemAPI.js'
import {
  GetAccounts, GetSelectedAccount, GetAccountStableKey, RemoveAccount,
  MoveAccountToTop,
} from '../../../wailsjs/go/bindings/AccountAPI.js'
import {
  GetAvatarUrl, UploadSkin, GetOfflineSkinCatalog, SetOfflineSkin,
} from '../../../wailsjs/go/bindings/AccountAPI.js'

const accounts = ref([])
const selectedKey = ref('')
const statusText = ref('')
const showLogin = ref(false)
// 头像缓存：stableKey → 头像 data URI
const avatarMap = ref({})
// 离线皮肤库
const showSkinCatalog = ref(false)
const skinCatalog = ref([])

const items = computed(() => accounts.value.map((account) => ({
  account,
  fallback: getFallbackText(account.DisplayName),
  isDefault: account.__stableKey === selectedKey.value,
  stableKey: account.__stableKey,
  avatarSource: avatarMap.value[account.__stableKey] || account.__avatar || '',
})))

const selected = computed({
  get: () => items.value.find((item) => item.stableKey === selectedKey.value) ?? null,
  set: (item) => { selectedKey.value = item?.stableKey ?? '' },
})

const selectedIsDefault = computed(() =>
  !!selected.value && selected.value.isDefault)

const selectedDetail = computed(() =>
  selected.value ? accountDetail(selected.value.account) : '')

function typeLabel(account) {
  switch (account.Type) {
    case 'microsoft': return '正版账户'
    case 'offline': return '离线账户'
    case 'authlib': return '皮肤站账户'
    default: return '第三方账户'
  }
}

function accountDetail(account) {
  switch (account.Type) {
    case 'microsoft':
      if (account.Microsoft) {
        const expired = new Date(account.Microsoft.ExpiresAt).getTime() <= Date.now()
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

function formatTime(value) {
  const date = new Date(typeof value === 'number' && value > 1e15 ? value / 1e6 : value)
  if (Number.isNaN(date.getTime())) return String(value ?? '')
  const pad = (n) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
    `${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function getFallbackText(value) {
  if (!value || !value.trim()) return '?'
  return value.trim()[0].toUpperCase()
}

async function refresh() {
  try {
    const list = await GetAccounts()
    const selectedAccount = await GetSelectedAccount().catch(() => null)
    let selectedStable = ''
    if (selectedAccount) selectedStable = await GetAccountStableKey(selectedAccount)
    const withKeys = await Promise.all(list.map(async (account) => ({
      ...account,
      __stableKey: await GetAccountStableKey(account),
    })))
    accounts.value = withKeys
    // 保持当前选中；未选中时回落到默认账号（列表首项）
    if (!withKeys.some((account) => account.__stableKey === selectedKey.value)) {
      selectedKey.value = selectedStable || withKeys[0]?.__stableKey || ''
    }
    statusText.value = withKeys.length === 0
      ? '还没有账号，点击「＋ 添加账户」开始。'
      : `共 ${withKeys.length} 个账号，列表首项为默认账号。`
    loadAvatars(withKeys)
  } catch (ex) {
    statusText.value = `读取账号列表失败：${ex?.message ?? ex}`
  }
}

// 异步加载真实皮肤头像（GetAvatarUrl 失败时保留首字母占位）
function loadAvatars(list) {
  list.forEach((account) => {
    const key = account.__stableKey
    GetAvatarUrl(key)
      .then((uri) => { if (uri) avatarMap.value[key] = uri })
      .catch(() => { /* 保留首字母占位 */ })
  })
}

function onAccountAdded(account) {
  showLogin.value = false
  statusText.value = `已添加账号：${account?.DisplayName ?? ''}`
  refresh()
}

async function setDefault() {
  const item = selected.value
  if (!item) {
    statusText.value = '请先选择一个账号。'
    return
  }
  try {
    await MoveAccountToTop(item.account)
    statusText.value = `已设为默认：${item.account.DisplayName}`
    await refresh()
    selectedKey.value = item.stableKey
  } catch (ex) {
    statusText.value = `设置默认账号失败：${ex?.message ?? ex}`
  }
}

async function removeAccount() {
  const item = selected.value
  if (!item) {
    statusText.value = '请先选择一个账号。'
    return
  }
  if (!(await nyaConfirm('删除账户', `确定删除账号「${item.account.DisplayName}」？该操作不可恢复。`))) {
    return
  }
  try {
    await RemoveAccount(item.account)
    const remaining = accounts.value.length - 1
    statusText.value = remaining === 0
      ? `已删除账号：${item.account.DisplayName}（账号列表已清空）`
      : `已删除账号：${item.account.DisplayName}`
    selectedKey.value = ''
    await refresh()
  } catch (ex) {
    statusText.value = `删除账号失败：${ex?.message ?? ex}`
  }
}

async function changeSkin() {
  const item = selected.value
  if (!item) {
    statusText.value = '请先选择一个账号。'
    return
  }
  const account = item.account
  if (account.Type === 'offline') {
    await changeOfflineSkinByFile(item)
    return
  }
  if (account.Type === 'authlib') {
    statusText.value = '皮肤站账号请到对应皮肤站的网页端更换皮肤。'
    return
  }
  if (account.Type === 'microsoft') {
    await changeMicrosoftSkin(item)
    return
  }
  statusText.value = '该账号暂不支持编辑皮肤。'
}

// 离线：选择本地 PNG 设为自定义皮肤（SetOfflineSkin 会复制到存储目录持久化）
async function changeOfflineSkinByFile(item) {
  let path = ''
  try {
    path = await SelectFile('选择皮肤贴图（png）', '图片文件', '*.png')
  } catch { /* 用户取消 */ }
  if (!path) return
  try {
    await SetOfflineSkin(item.stableKey, path)
    statusText.value = `已设置离线自定义皮肤：${path.split(/[\\/]/).pop()}`
    await refresh()
    selectedKey.value = item.stableKey
    alert('离线自定义皮肤已应用。')
  } catch (ex) {
    statusText.value = `更换皮肤失败：${ex?.message ?? ex}`
    alert(String(ex?.message ?? ex))
  }
}

// 离线皮肤库：内置 9 款皮肤网格（对应 OfflineSkinPickerDialog）
async function openSkinCatalog() {
  const item = selected.value
  if (!item || item.account.Type !== 'offline') {
    statusText.value = '皮肤库仅支持离线账号。'
    return
  }
  try {
    skinCatalog.value = (await GetOfflineSkinCatalog()) ?? []
    showSkinCatalog.value = true
  } catch (ex) {
    statusText.value = `读取皮肤库失败：${ex?.message ?? ex}`
  }
}

async function applyCatalogSkin(choice) {
  const item = selected.value
  if (!item) return
  showSkinCatalog.value = false
  try {
    await SetOfflineSkin(item.stableKey, choice.id)
    statusText.value = `已选择离线默认皮肤：${choice.displayName}`
    await refresh()
    selectedKey.value = item.stableKey
  } catch (ex) {
    statusText.value = `更换皮肤失败：${ex?.message ?? ex}`
  }
}

// 正版：选 png → 选模型（slim/classic）→ UploadSkin（对应 MinecraftAppearanceEditor）
async function changeMicrosoftSkin(item) {
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
    statusText.value = '正版皮肤已更新。'
    await refresh()
    selectedKey.value = item.stableKey
    alert('正版皮肤已更新，头像即将刷新（服务端可能有几分钟缓存延迟）。')
  } catch (ex) {
    statusText.value = `皮肤上传失败：${ex?.message ?? ex}`
    alert(String(ex?.message ?? ex))
  }
}

async function changeCape() {
  const item = selected.value
  if (item?.account.Type !== 'microsoft') {
    statusText.value = '更换披风仅支持正版账号。'
    return
  }
  // PORTING: C# 披风流程为 GetProfileAsync 读披风列表 → CapeSelectionDialog →
  // SetActiveCapeAsync（激活/停用已有披风；披风不支持上传）。绑定层暂未移植
  // 披风激活 API，保留现状提示。
  statusText.value = '披风不支持上传；披风切换（激活/停用已有披风）暂未开放，敬请期待。'
}

onMounted(refresh)
</script>
