<template>
  <div class="relative isolate h-screen grid overflow-hidden bg-background text-foreground"
    style="grid-template-rows: var(--titlebar-height) 1fr var(--statusbar-height);">

    <!-- 个性化特效层：自定义背景壁纸（最底层）+ 彩虹背景/星尘/点击圆环 -->
    <FxLayer />

    <!-- TooltipProvider 必须包裹全应用：导航/标题栏/状态栏的任何 Tooltip 都依赖它 -->
    <TooltipProvider>

    <!-- Row 0：自定义标题栏（58px）。身份区/空白区可拖拽移动窗口 -->
    <header class="grid grid-cols-[auto_1fr_auto] items-center border-b border-subtle-border bg-background pr-2.5 shadow-[0_1px_6px_rgba(0,0,0,0.08)]"
      style="--wails-draggable:drag">

      <!-- 左：应用身份 -->
      <div class="flex items-center gap-2.5 pl-[19px] pr-[22px]" style="--wails-draggable:noDrag">
        <div class="grid size-9 place-items-center rounded-[11px] bg-accent-dark">
          <span class="grid size-7 place-items-center rounded-[7px] border border-medium-border bg-primary font-bold text-[14px] text-white">N</span>
        </div>
        <div class="flex flex-col">
          <div class="flex items-center gap-2">
            <span class="text-[15px] font-semibold text-primary-text">NyaLauncher</span>
            <Badge class="rounded-[7px] px-[7px] py-0.5 text-[8px]">PREVIEW 预览版本</Badge>
          </div>
          <span class="text-[10px] text-hint-text">{{ appVersion }}</span>
        </div>
      </div>

      <!-- 中：返回按钮（进入页面导航时显示）+ 页面标题 + 拖拽空白 -->
      <div class="flex h-full items-center gap-3">
        <Button v-if="isPageRoute" variant="secondary" class="nav-back-button btn-anim h-8 bg-surface px-3.5 border-strong-border text-body-text"
          style="--wails-draggable:noDrag" @click="goHome">
          <span class="icon">←</span>
          <span>返回工作区</span>
        </Button>
        <span v-if="pageTitle" class="text-[13px] font-semibold text-secondary-text">{{ pageTitle }}</span>
      </div>

      <!-- 右：窗口控制（42×34，关闭钮 hover 红底白字） -->
      <div class="flex gap-1" style="--wails-draggable:noDrag">
        <Tooltip>
          <TooltipTrigger as-child>
            <button class="window-button" @click="minimise">
              <svg width="12" height="12" viewBox="0 0 12 12"><rect x="0.5" y="8.8" width="11" height="1.8" fill="currentColor"/></svg>
            </button>
          </TooltipTrigger>
          <TooltipContent>最小化</TooltipContent>
        </Tooltip>
        <Tooltip>
          <TooltipTrigger as-child>
            <button class="window-button" @click="toggleMaximise">
              <svg v-if="!maximised" width="12" height="12" viewBox="0 0 12 12">
                <rect x="1.5" y="1.5" width="9" height="9" fill="none" stroke="currentColor" stroke-width="1.4"/>
              </svg>
              <svg v-else width="12" height="12" viewBox="0 0 12 12">
                <path d="M 3.5,1.5 L 10.5,1.5 L 10.5,8.5 M 1.5,3.5 L 8.5,3.5 L 8.5,10.5 L 1.5,10.5 Z" fill="none" stroke="currentColor" stroke-width="1.4"/>
              </svg>
            </button>
          </TooltipTrigger>
          <TooltipContent>最大化或还原</TooltipContent>
        </Tooltip>
        <Tooltip>
          <TooltipTrigger as-child>
            <button class="window-button close" @click="quit">
              <svg width="12" height="12" viewBox="0 0 12 12">
                <path d="M 2,2 L 10,10 M 10,2 L 2,10" stroke="currentColor" stroke-width="1.4"/>
              </svg>
            </button>
          </TooltipTrigger>
          <TooltipContent>关闭</TooltipContent>
        </Tooltip>
      </div>
    </header>

    <!-- Row 1：导航侧栏（52/176 可展开）+ 内容区 -->
    <div class="flex min-h-0">
      <nav
        class="nav-rail flex shrink-0 flex-col justify-between overflow-hidden bg-background border-r border-subtle-border py-3"
        :class="{ expanded: navExpanded }"
        style="transition: width var(--dur-medium) var(--ease-emphasized);"
      >
        <div class="flex flex-col gap-1">
          <Tooltip v-for="item in navItems" :key="item.to" :delay-duration="navExpanded ? 800 : 300">
            <TooltipTrigger as-child>
              <button
                class="nav-rail-item btn-anim"
                :class="{ active: isActive(item) }"
                @click="router.push(item.to)"
              >
                <Icon class="nav-icon" :name="item.icon" :size="18" />
                <span class="nav-label">{{ item.label }}</span>
              </button>
            </TooltipTrigger>
            <TooltipContent side="right">{{ item.label }}</TooltipContent>
          </Tooltip>
        </div>
        <Tooltip>
          <TooltipTrigger as-child>
            <button class="nav-rail-item btn-anim" @click="navExpanded = !navExpanded">
              <Icon class="nav-icon" :name="navExpanded ? 'nav-chevron-left' : 'nav-chevron-right'" :size="18" />
              <span class="nav-label">{{ navExpanded ? '收起' : '展开' }}</span>
            </button>
          </TooltipTrigger>
          <TooltipContent side="right">{{ navExpanded ? '收起侧栏' : '展开侧栏' }}</TooltipContent>
        </Tooltip>
      </nav>

      <main class="min-w-0 min-h-0 flex-1 overflow-y-auto">
        <router-view v-slot="{ Component }">
          <transition name="page" mode="out-in">
            <component :is="Component" />
          </transition>
        </router-view>
      </main>
    </div>

    <!-- Row 2：状态栏（30px） -->
    <footer class="flex items-center justify-between px-4 border-t border-subtle-border bg-background">
      <div>
        <span class="text-[9px] text-warning">测试版 · 功能不稳定，不建议日常使用</span>
      </div>
      <div class="flex items-center gap-1">
        <Button size="sm" variant="outline" class="add-component-button btn-anim h-6 px-2.5" :class="{ 'edit-active': editMode }"
          @click="editMode = !editMode"
        >{{ editMode ? '退出编辑' : '编辑模式' }}</Button>
        <!-- 任务活动指示（原版 MainWindow TaskActivityButton）：有活动下载/导出任务时亮起，点击打开下载页 -->
        <Tooltip>
          <TooltipTrigger as-child>
            <button class="task-activity" :class="{ active: taskActive }" @click="router.push('/download')">
              <span class="block size-2 rounded-full" :class="taskActive ? 'bg-primary' : 'bg-hint-text/40'" />
            </button>
          </TooltipTrigger>
          <TooltipContent>{{ taskActive ? '有进行中的任务，点击查看' : '暂无进行中的任务' }}</TooltipContent>
        </Tooltip>
        <Button size="sm" variant="outline" class="add-component-button btn-anim h-6 px-2.5" @click="logOpen = true">日志</Button>
        <Tooltip>
          <TooltipTrigger as-child>
            <button class="settings-quick" @click="router.push('/settings')">
              <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor">
                <path d="M12 15.5A3.5 3.5 0 1 1 12 8.5a3.5 3.5 0 0 1 0 7zm7.4-2.6c.04-.3.07-.6.07-.9s-.02-.6-.07-.9l2-1.6a.5.5 0 0 0 .12-.64l-1.9-3.3a.5.5 0 0 0-.6-.22l-2.4 1a7 7 0 0 0-1.6-.94l-.36-2.5A.5.5 0 0 0 14.2 2h-3.8a.5.5 0 0 0-.5.42l-.35 2.5c-.58.24-1.12.56-1.6.94l-2.4-1a.5.5 0 0 0-.61.22l-1.9 3.3a.5.5 0 0 0 .12.64l2 1.6c-.04.3-.07.6-.07.9s.02.6.07.9l-2 1.6a.5.5 0 0 0-.12.64l1.9 3.3c.13.22.39.31.6.22l2.4-1c.5.38 1.03.7 1.6.94l.36 2.5c.04.24.25.42.5.42h3.8c.25 0 .46-.18.5-.42l.35-2.5a7 7 0 0 0 1.6-.94l2.4 1c.23.09.49 0 .61-.22l1.9-3.3a.5.5 0 0 0-.12-.64l-2-1.6z"/>
              </svg>
            </button>
          </TooltipTrigger>
          <TooltipContent>打开设置</TooltipContent>
        </Tooltip>
      </div>
    </footer>

    <!-- 启动日志遮罩（Controls/GameLogOverlay 移植） -->
    <GameLogOverlay :open="logOpen" @close="logOpen = false" />
    </TooltipProvider>
  </div>
</template>

<script setup>
import { computed, onMounted, provide, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { WindowMinimise, WindowToggleMaximise, Quit, EventsOn } from '../wailsjs/runtime/runtime.js';
import { GetAppVersion } from '../wailsjs/go/bindings/SystemAPI.js';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { TooltipProvider, Tooltip, TooltipTrigger, TooltipContent } from '@/components/ui/tooltip';
import Icon from '@/components/overlay/Icon.vue';
import FxLayer from '@/components/overlay/FxLayer.vue';
import GameLogOverlay from '@/components/overlay/GameLogOverlay.vue';

const route = useRoute();
const router = useRouter();

const navExpanded = ref(false);
const editMode = ref(false);
// 主页组件画布（pages/home/ComponentCanvas.vue）经 inject 读取编辑模式；editMode 逻辑零改动（移植最小改动）
provide('homeEditMode', editMode);
const maximised = ref(false);
// 启动日志遮罩开关（状态栏「日志」按钮）
const logOpen = ref(false);
// 任务活动指示：下载 / 集成包导出有活动任务时亮起（对应原版 TaskActivityButton）
const taskActive = ref(false);
EventsOn('download:progress', (snap) => {
  const phase = Number(snap?.Phase ?? 0);
  if (phase === 1 || phase === 2) { taskActive.value = true; return; }
  // 非活跃阶段（完成/失败/取消/空闲）时延迟复核，避免闪烁；简化为直接熄灭
  taskActive.value = false;
});
EventsOn('modpack:exportProgress', (p) => { taskActive.value = (p?.Total ?? 0) > 0; });

// 窗口最大化状态事件（Go 端须 EventsEmit("window:maximised", bool)；未接时按钮图标保持默认）
EventsOn('window:maximised', (v) => { maximised.value = !!v; });

const navItems = [
  { to: '/download', label: '资源下载', icon: 'nav-download' },
  { to: '/versions', label: '实例管理', icon: 'nav-instances' },
  { to: '/settings/account', label: '账号管理', icon: 'nav-account' },
  { to: '/music', label: '音乐播放器', icon: 'nav-music' },
  { to: '/settings', label: '设置', icon: 'nav-settings' },
];

const isPageRoute = computed(() => route.path !== '/');
// 标题取当前实际路由（matched 最深记录）的 meta.title，避免标题与内容页不同步
const pageTitle = computed(() => {
  const matched = route.matched ?? [];
  return matched[matched.length - 1]?.meta?.title ?? '';
});

function isActive(item) {
  if (item.to === '/settings') {
    // 有更具体的子路由导航项（如 /settings/account）时不再点亮「设置」
    if (route.path.startsWith('/settings') && navItems.some((n) => n.to !== '/settings' && isActive(n))) return false;
    return route.path.startsWith('/settings');
  }
  return route.path === item.to;
}

function goHome() { router.push('/'); }
function minimise() { WindowMinimise(); }
function toggleMaximise() {
  WindowToggleMaximise();
  // ToggleMaximise 无返回值，乐观翻转图标（Go 端如 EventsEmit window:maximised 会覆盖）
  maximised.value = !maximised.value;
}
function quit() { Quit(); }

// 版本号经 SystemAPI.GetAppVersion 动态获取，失败时回落占位
const appVersion = ref('v0.0.0-dev');
onMounted(async () => {
  try {
    const v = await GetAppVersion();
    if (v) appVersion.value = v.startsWith('v') ? v : `v${v}`;
  } catch { /* 保留占位 */ }
});
</script>

<style scoped>
/* 保留原版导航项语义（尺寸/展开态/高亮），布局用 Tailwind 工具类完成 */
.nav-rail-item {
  height: 40px;
  width: 40px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  gap: 12px;
  border-radius: var(--radius-lg);
  color: var(--body-text);
  font-size: var(--font-body);
  white-space: nowrap;
  transition: background-color 0.15s var(--ease-emphasized), width var(--dur-medium) var(--ease-emphasized);
}
.nav-rail-item:hover { background: var(--control-bg); opacity: 1; }
.nav-rail-item.active { background: var(--highlight-bg); color: var(--accent-text-color); }
.nav-rail { width: var(--navrail-collapsed); }
.nav-rail.expanded { width: var(--navrail-expanded); }
.nav-rail.expanded .nav-rail-item { width: calc(100% - 12px); padding: 0 12px; }
.nav-icon { width: 18px; text-align: center; flex-shrink: 0; color: var(--subtext-text); }
.nav-rail-item.active .nav-icon { color: var(--accent-text-color); }
.nav-label { display: none; }
.nav-rail.expanded .nav-label { display: inline; }

.settings-quick {
  width: 22px;
  height: 22px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  color: var(--subtext-text);
}
.settings-quick:hover { background: var(--control-bg); opacity: 1; }

/* 任务活动指示（TaskActivityButton）：小圆点，活跃时常亮 + 脉冲 */
.task-activity {
  width: 22px;
  height: 22px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  opacity: 0.7;
}
.task-activity:hover { background: var(--control-bg); opacity: 1; }
.task-activity.active { opacity: 1; }
.task-activity.active span { animation: task-pulse 1.6s ease-in-out infinite; }
@keyframes task-pulse {
  0%, 100% { box-shadow: 0 0 0 0 color-mix(in srgb, var(--accent) 55%, transparent); }
  50% { box-shadow: 0 0 0 4px color-mix(in srgb, var(--accent) 0%, transparent); }
}
</style>
