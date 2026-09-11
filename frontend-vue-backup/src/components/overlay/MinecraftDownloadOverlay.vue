<template>
  <!--
    MinecraftDownloadOverlay.vue（移植自 Controls/MinecraftDownloadOverlay.axaml）：
    版本下载确认遮罩 —— Loader 类型卡片 / Loader 版本 / 实例名 / 状态提示 / 取消+下载。
    由父级 ModalOverlayHost 承载；确认时 emit('confirm', options)，取消 emit('close')。
  -->
  <div class="flex max-h-[620px] w-[540px] flex-col gap-[14px] rounded-2xl bg-background px-6 pb-[22px] pt-5">
    <!-- 标题栏（OverlayHeader.axaml：44 徽标 + 标题/副标题 + 圆形关闭钮） -->
    <div class="flex items-center gap-3">
      <div class="flex size-11 shrink-0 items-center justify-center rounded-[10px] bg-badge text-[20px]">⛏</div>
      <div class="flex min-w-0 flex-1 flex-col gap-0.5">
        <span class="truncate text-base font-bold text-foreground">下载 Minecraft</span>
        <span class="truncate text-[11px] text-hint-text">{{ subtitle }}</span>
      </div>
      <button
        class="flex size-[30px] shrink-0 cursor-pointer items-center justify-center rounded-full bg-secondary text-subtext-text transition-opacity hover:opacity-[var(--hover-opacity)]"
        @click="onCancel"
      >
        <X :size="14" />
      </button>
    </div>

    <!-- Loader 类型（RadioButton.LoaderCard：ControlBg r10 卡片，选中 HighlightBg + Accent 边） -->
    <div class="flex flex-col gap-2">
      <span class="text-[13px] font-semibold text-secondary-text">选择加载器</span>
      <div class="grid grid-cols-4 gap-2">
        <button
          v-for="lt in loaderTypes"
          :key="lt.value"
          class="cursor-pointer rounded-[10px] border border-transparent bg-muted px-2.5 py-[9px] text-[13px] font-semibold text-body-text transition-colors"
          :class="loaderType === lt.value ? 'border-border bg-accent !text-primary' : 'hover:bg-accent'"
          @click="setLoaderType(lt.value)"
        >
          {{ lt.label }}
        </button>
      </div>
    </div>

    <!-- Loader 版本（原版时隐藏；切换时淡入 200ms emphasized） -->
    <transition name="nya-fade">
      <div v-if="loaderType !== 0" class="flex flex-col gap-2">
        <span class="text-[13px] font-semibold text-secondary-text">加载器版本</span>
        <Select
          v-model="loaderVersionIndex"
          :disabled="loaderLoading || loaderVersions.length === 0"
        >
          <SelectTrigger class="w-full rounded-lg border-none bg-muted px-3 py-[7px] text-[13px]">
            <SelectValue :placeholder="loaderLoading ? '正在加载版本列表…' : (loaderVersions.length ? '选择加载器版本' : '无可用版本')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="(lv, i) in loaderVersions" :key="lv.LoaderVersion" :value="String(i)">
              {{ lv.LoaderVersion }}{{ lv.IsStable ? '' : '（非稳定）' }}
            </SelectItem>
          </SelectContent>
        </Select>
        <span v-if="loaderHint" class="text-[11px] leading-relaxed text-hint-text">{{ loaderHint }}</span>
        <label v-if="loaderType === 1" class="nya-fade flex cursor-pointer items-center gap-2 text-xs text-body-text">
          <Switch v-model="skipFabricApi" />
          不主动下载 Fabric API
        </label>
      </div>
    </transition>

    <!-- 实例名称 -->
    <div class="flex flex-col gap-2">
      <span class="text-[13px] font-semibold text-secondary-text">实例名称</span>
      <Input v-model="instanceName" placeholder="留空则使用默认名称" class="h-auto rounded-lg border-none bg-muted px-3 py-2 text-[13px]" />
      <span v-if="instanceNameHint" class="text-[11px] leading-relaxed text-hint-text">{{ instanceNameHint }}</span>
    </div>

    <!-- 状态提示（错误） -->
    <span v-if="statusText" class="text-xs leading-relaxed text-destructive">{{ statusText }}</span>

    <!-- 底部按钮：取消 / 下载 -->
    <div class="flex justify-end gap-2">
      <Button variant="secondary" class="h-auto px-[18px] py-2 text-[13px]" @click="onCancel">取消</Button>
      <Button class="h-auto gap-1.5 px-5 py-2 text-[13px] font-semibold" @click="onDownload">
        <ArrowDown :size="15" /><span>下载</span>
      </Button>
    </div>
  </div>
</template>

<script setup>
/*
 * 移植自 Controls/MinecraftDownloadOverlay.axaml.cs：
 * - Setup(version) 由父级在打开时通过 prop version 触发（watch 复位表单）；
 * - Loader 元数据经 DownloadAPI.GetModLoaderVersions（枚举索引与 Go ModLoaderType 对齐：
 *   Vanilla=0 / Fabric=1 / Quilt=2 / NeoForge=3 / Forge=4）；
 * - 默认实例名经 DownloadAPI.CreateDefaultInstanceName；
 * - 确认时 emit('confirm', { loaderType, loaderVersion, instanceName, skipFabricApi })，
 *   由 DownloadView 决定 StartDownload / StartModLoaderDownload。
 */
import { ref, watch } from 'vue'
import { ArrowDown, X } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { CreateDefaultInstanceName, GetModLoaderVersions } from '../../../wailsjs/go/bindings/DownloadAPI'

const props = defineProps({
  version: { type: Object, default: null }, // models.MinecraftVersion { id, type, ... }
})
const emit = defineEmits(['close', 'confirm'])

const loaderTypes = [
  { value: 0, label: '原版' },
  { value: 1, label: 'Fabric' },
  { value: 3, label: 'NeoForge' },
  { value: 4, label: 'Forge' },
]

const loaderType = ref(0)
const loaderVersions = ref([])
const loaderVersionIndex = ref('')
const loaderLoading = ref(false)
const loaderHint = ref('')
const skipFabricApi = ref(false)
const instanceName = ref('')
const instanceNameHint = ref('')
const statusText = ref('')

const subtitle = ref('')
let loadSeq = 0

watch(
  () => props.version,
  (v) => {
    if (!v) return
    // ResetState：复位表单并预填实例名
    loaderType.value = 0
    loaderVersions.value = []
    loaderVersionIndex.value = ''
    loaderHint.value = ''
    skipFabricApi.value = false
    statusText.value = ''
    instanceName.value = v.id
    instanceNameHint.value = `版本将安装至 versions/${v.id}/`
    subtitle.value = `${v.id}（${typeDisplay(v.type)}）`
  },
  { immediate: true },
)

function typeDisplay(t) {
  return { release: '正式版', snapshot: '快照版', old_alpha: '远古版本', old_beta: '远古版本' }[t] ?? t
}

async function setLoaderType(t) {
  if (loaderType.value === t) return
  loaderType.value = t
  statusText.value = ''
  const mcId = props.version?.id
  if (!mcId) return
  if (t === 0) {
    loaderVersions.value = []
    loaderVersionIndex.value = ''
    loaderHint.value = ''
    instanceName.value = mcId
    instanceNameHint.value = `版本将安装至 versions/${mcId}/`
    return
  }
  // OnLoaderTypeChanged：拉取 Loader 元数据，优先选中稳定版
  const seq = ++loadSeq
  loaderLoading.value = true
  loaderVersions.value = []
  loaderVersionIndex.value = ''
  loaderHint.value = '正在获取可用版本…'
  try {
    const list = (await GetModLoaderVersions(t, mcId)) ?? []
    if (seq !== loadSeq) return // 快速切换时丢弃旧结果
    loaderVersions.value = list
    if (list.length > 0) {
      const preferred = list.findIndex((v) => v.IsStable)
      selectLoaderVersion(preferred >= 0 ? preferred : 0)
    } else {
      loaderHint.value = '该 Minecraft 版本暂无可用的加载器版本。'
    }
  } catch (e) {
    if (seq !== loadSeq) return
    loaderHint.value = `获取版本列表失败：${e?.message ?? e}`
  } finally {
    if (seq === loadSeq) loaderLoading.value = false
  }
}

async function selectLoaderVersion(i) {
  const lv = loaderVersions.value[i]
  if (!lv) return
  loaderVersionIndex.value = String(i)
  loaderHint.value = lv.IsStable
    ? '推荐版本'
    : '非稳定版本，可能存在兼容性问题'
  try {
    // CreateDefaultInstanceName(loaderType, loaderVersion, mcVersion)
    const name = await CreateDefaultInstanceName(loaderType.value, lv.LoaderVersion, props.version.id)
    instanceName.value = name
    instanceNameHint.value = `版本将安装至 versions/${name}/`
  } catch (e) {
    console.error('生成默认实例名失败', e)
  }
}

function onLoaderVersionChange() {
  const i = Number(loaderVersionIndex.value)
  if (!Number.isNaN(i)) selectLoaderVersion(i)
}
watch(loaderVersionIndex, onLoaderVersionChange)

// OverlayHelpers.IsValidInstanceName：拒绝空 / "." ".." / 非法文件名字符 / 路径分隔符
function validateInstanceName(name) {
  if (!name || !name.trim()) return '请输入实例名称。'
  if (name === '.' || name === '..') return '实例名称非法。'
  if (/[\\/:*?"<>|]/.test(name)) return '实例名包含不安全字符，请换一个名字。'
  return ''
}

function onCancel() {
  emit('close')
}

function onDownload() {
  if (!props.version) return
  let loaderVersion = null
  if (loaderType.value !== 0) {
    const i = Number(loaderVersionIndex.value)
    loaderVersion = loaderVersions.value[i] ?? null
    if (!loaderVersion) {
      statusText.value = '请选择加载器版本。'
      return
    }
  }
  const fallback = loaderVersion
    ? `${props.version.id}-${loaderVersion.LoaderVersion}`
    : props.version.id
  const name = (instanceName.value || '').trim() || fallback
  const nameErr = validateInstanceName(name)
  if (nameErr) {
    statusText.value = nameErr
    return
  }
  emit('confirm', {
    loaderType: loaderType.value,
    loaderVersion,
    instanceName: name,
    skipFabricApi: skipFabricApi.value,
  })
}
</script>

<style scoped>
/* FadeInAsync(200ms, emphasized-decelerate) 的等价过渡（Loader 版本区段 / Fabric API 选项） */
.nya-fade-enter-active { transition: opacity 200ms var(--ease-emphasized-decelerate); }
.nya-fade-leave-active { transition: opacity 150ms var(--ease-emphasized-accelerate); }
.nya-fade-enter-from, .nya-fade-leave-to { opacity: 0; }
</style>
