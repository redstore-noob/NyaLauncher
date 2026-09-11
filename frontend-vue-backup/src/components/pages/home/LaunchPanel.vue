<template>
  <!-- 右侧常驻「游戏启动区」：账户 + 版本 + 启动按钮（GameLaunchPanel.axaml） -->
  <aside class="h-full overflow-y-auto border-l border-subtle-border bg-background">
    <div class="mx-3.5 my-3 flex h-[calc(100%-24px)] flex-col">
      <!-- 标题 -->
      <div class="mb-2.5 ml-0.5 flex items-center gap-2">
        <span class="text-[15px] font-semibold text-foreground">快速启动</span>
        <Badge>{{ versions.length }} 个版本</Badge>
      </div>

      <!-- 账户行：头像 + 账户选择 -->
      <Card class="flex items-center gap-3 rounded-xl p-2.5 px-3">
        <div
          class="flex size-11 shrink-0 items-center justify-center overflow-hidden rounded-md bg-badge"
        >
          <span class="text-lg font-bold text-primary">{{ avatarFallback }}</span>
          <!-- TODO: 皮肤头像 AsyncImage（离线皮肤/皮肤站/正版档案解析）待接 -->
        </div>
        <div class="flex min-w-0 flex-1 flex-col gap-1">
          <Select
            :model-value="selectedAccountKey"
            @update:model-value="onAccountChange"
          >
            <SelectTrigger class="w-full">
              <SelectValue placeholder="选择账户" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="a in accounts"
                :key="accountKey(a)"
                :value="accountKey(a)"
              >
                {{ a.DisplayName }}（{{ typeLabel(a) }}）
              </SelectItem>
            </SelectContent>
          </Select>
          <span class="truncate text-[10px] text-hint-text">
            {{ selectedAccount ? `${typeLabel(selectedAccount)} · ${selectedAccount.DisplayName}` : '尚无账户，点击右上角「＋」添加' }}
          </span>
        </div>
      </Card>

      <!-- 版本列表头 -->
      <div class="mb-1.5 ml-0.5 mt-3 text-[13px] font-semibold text-secondary-text">游戏版本</div>

      <!-- 版本选择 -->
      <div class="flex flex-1 flex-col gap-1.5">
        <template v-if="versions.length > 0">
          <Select v-model="selectedVersion" @update:model-value="onVersionChange">
            <SelectTrigger class="w-full">
              <SelectValue placeholder="选择游戏版本" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="v in versions" :key="v" :value="v">{{ displayNames[v] || v }}</SelectItem>
            </SelectContent>
          </Select>
          <span class="truncate text-[10px] text-hint-text">启动前可在此切换版本</span>
        </template>
        <div v-else class="flex flex-1 flex-col items-center justify-center gap-2">
          <span class="text-[10px] text-hint-text">还没有游戏版本</span>
          <Button variant="secondary" size="sm" @click="goDownloads">前往资源下载</Button>
        </div>
      </div>

      <!-- 启动区 -->
      <div class="mt-2.5 flex flex-col items-stretch gap-1.5">
        <span class="text-center text-[10px] text-hint-text">{{ statusText }}</span>
        <span v-if="memoryHint" class="text-center text-[10px] text-hint-text">{{ memoryHint }}</span>
        <Button
          class="h-11 gap-2 rounded-xl border border-accent-bright bg-accent-deep text-sm font-semibold text-[var(--white)] hover:bg-accent-deep"
          :disabled="!canLaunch"
          @click="onLaunch"
        >
          <span class="text-xs">▶</span>
          <span>{{ launchButtonText }}</span>
        </Button>
        <Button
          v-if="!selectedAccount"
          variant="link"
          size="sm"
          class="text-[11px] text-[var(--link-text-color)] no-underline hover:underline"
          @click="goAccount"
        >
          还没有账户？去添加一个 →
        </Button>
      </div>
    </div>
  </aside>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { GetAccounts, GetSelectedAccount, GetAccountStableKey, MoveAccountToTop, SelectAccountByStableKey } from '../../../../wailsjs/go/bindings/AccountAPI'
import { GetCurrentInstanceSnapshot, SelectInstance, GetInstanceDisplayVersion } from '../../../../wailsjs/go/bindings/InstanceAPI'
import { GetLaunchSnapshot, GetMemoryDecision, Launch } from '../../../../wailsjs/go/bindings/LauncherAPI'
import { alert as nyaAlert } from '@/composables/dialog.js'
import { EventsOn, EventsOff } from '../../../../wailsjs/runtime/runtime'

const router = useRouter()

const accounts = ref([])
const selectedAccount = ref(null)
const versions = ref([])
const selectedVersion = ref('')
const busy = ref(false)
const statusText = ref('')
const memoryHint = ref('')

const selectedAccountKey = computed({
  get: () => (selectedAccount.value ? accountKey(selectedAccount.value) : ''),
  set: () => {},
})

const avatarFallback = computed(() => {
  const name = selectedAccount.value?.DisplayName ?? ''
  return name ? name[0].toUpperCase() : '?'
})

const launchButtonText = computed(() => {
  if (busy.value) return statusText.value || '正在启动…'
  return '启动游戏'
})

const canLaunch = computed(
  () => !busy.value && !!selectedAccount.value && !!selectedVersion.value
)

function accountKey(a) {
  // 同步 GetAccountStableKey 成本高，这里用 Type+Name 近似；选择时再走真实 StableKey
  return `${a.Type}:${a.DisplayName}`
}

function typeLabel(a) {
  return { offline: '离线', microsoft: '正版', authlib: '外置登录' }[a.Type] ?? a.Type
}

async function loadAccounts() {
  try {
    accounts.value = (await GetAccounts()) ?? []
    selectedAccount.value = await GetSelectedAccount().catch(() => null)
  } catch (e) {
    console.error('加载账户失败', e)
    await nyaAlert('加载账户失败：' + (e?.message ?? e), { severity: 'error' })
  }
}

async function onAccountChange(key) {
  const found = accounts.value.find((a) => accountKey(a) === key)
  if (!found) return
  selectedAccount.value = found
  try {
    const stable = await GetAccountStableKey(found)
    await SelectAccountByStableKey(stable)
    await MoveAccountToTop(found) // 原版语义：选中即置顶
    accounts.value = (await GetAccounts()) ?? []
  } catch (e) {
    console.error('切换账户失败', e)
  }
}

const displayNames = ref({})

function applySnapshot(snap) {
  if (!snap) return
  versions.value = snap.VersionIds ?? []
  selectedVersion.value = snap.SelectedVersionId ?? ''
  // 解析显示版本号（加载器实例目录名 ≠ MC 版本，如 fabric-loader-x 显示其继承的 MC 版本）
  for (const v of versions.value) {
    if (displayNames.value[v]) continue
    GetInstanceDisplayVersion(v)
      .then((d) => { displayNames.value[v] = d || v })
      .catch(() => { displayNames.value[v] = v })
  }
}

async function onVersionChange() {
  if (!selectedVersion.value) return
  try {
    await SelectInstance(selectedVersion.value)
  } catch (e) {
    console.error('切换版本失败', e)
  }
}

function applyLaunchState(snap) {
  if (!snap) return
  busy.value = !!snap.Title || !!snap.Message
  if (busy.value) statusText.value = snap.Message || '正在准备游戏环境…'
  else if (!statusText.value) statusText.value = ''
}

async function onLaunch() {
  if (!selectedAccount.value) return nyaAlert('请先选择账户', { severity: 'warning' })
  if (!selectedVersion.value) return nyaAlert('请先选择游戏版本', { severity: 'warning' })
  busy.value = true
  statusText.value = '正在启动…'
  try {
    // Launch 阻塞至启动流程结束，Wails 在后台 goroutine 执行
    const result = await Launch('', null) // ctx 占位（见 PORTING_NOTES）
    statusText.value = result?.Success ? '游戏进程已创建。' : result?.Message || '启动失败。'
  } catch (e) {
    statusText.value = `启动失败：${e}`
    console.error('启动失败', e)
  } finally {
    busy.value = false
    loadMemoryHint()
  }
}

async function loadMemoryHint() {
  try {
    const decision = await GetMemoryDecision(null)
    if (decision) {
      memoryHint.value = `系统内存 ${(decision.TotalMemoryMb / 1024).toFixed(1)} GiB · 自动策略上限 ${decision.MaximumMemoryMb} MiB`
    }
  } catch (e) {
    console.error('读取内存策略失败', e)
  }
}

const goDownloads = () => router.push('/download')
const goAccount = () => router.push('/settings/account')

function onInstanceChanged(snap) {
  applySnapshot(snap)
}
function onLaunchChanged(snap) {
  applyLaunchState(snap)
}

onMounted(async () => {
  await Promise.all([loadAccounts(), loadMemoryHint()])
  try {
    applySnapshot(await GetCurrentInstanceSnapshot())
    applyLaunchState(await GetLaunchSnapshot())
  } catch (e) {
    console.error('初始化启动面板失败', e)
  }
  EventsOn('instance:changed', onInstanceChanged)
  EventsOn('launch:changed', onLaunchChanged)
})

onUnmounted(() => {
  EventsOff('instance:changed')
  EventsOff('launch:changed')
})
</script>
