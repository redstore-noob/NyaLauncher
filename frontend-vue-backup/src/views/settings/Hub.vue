<template>
  <section class="flex h-full flex-col overflow-hidden">
    <!-- 顶部标题栏 -->
    <header class="flex items-center justify-between gap-4 border-b border-subtle-border bg-background px-6 pb-3 pt-4">
      <div class="flex flex-col gap-1">
        <h1 class="m-0 text-[21px] font-bold text-primary-text">设置</h1>
        <p class="m-0 text-[11px] text-hint-text">启动器配置、运行环境与个性化</p>
      </div>
      <Input
        v-model="searchQuery"
        type="search"
        placeholder="搜索设置…"
        class="w-[260px] rounded-full bg-muted border-none focus-visible:ring-1 focus-visible:ring-ring/40"
        @keydown.esc.prevent="searchQuery = ''"
      />
    </header>

    <div class="grid min-h-0 flex-1 grid-cols-[220px_1fr]">
      <!-- 左侧标签栏 -->
      <nav class="flex flex-col gap-1 overflow-y-auto border-r border-subtle-border bg-background px-3">
        <span class="mx-2 mt-4 mb-2 text-[10px] font-bold tracking-widest text-muted-text">分类</span>
        <button
          v-for="tab in tabs"
          :key="tab.key"
          class="flex cursor-pointer flex-col gap-1 rounded-2xl border-none bg-transparent px-4 py-2 text-left transition-colors duration-150"
          :class="activeTab === tab.key ? 'bg-accent' : 'hover:bg-muted'"
          @click="switchTab(tab.key)"
        >
          <span class="flex items-center gap-2">
            <span class="text-[15px]" :class="activeTab === tab.key ? 'text-primary' : 'text-subtext-text'">{{ tab.icon }}</span>
            <span
              class="text-[13px] font-semibold"
              :class="activeTab === tab.key ? 'text-primary-text' : 'text-subtext-text'"
            >{{ tab.title }}</span>
            <Badge
              v-if="searching && counts[tab.key] >= 0"
              class="ml-auto rounded-sm bg-badge px-2 py-0.5 text-[9px] font-bold text-primary"
            >{{ counts[tab.key] }}</Badge>
          </span>
          <span class="text-[10px] text-hint-text">{{ tab.desc }}</span>
        </button>
      </nav>

      <!-- 右侧内容区 -->
      <div class="relative min-w-0 overflow-y-auto">
        <Transition name="page" mode="out-in">
          <LauncherSettings v-if="activeTab === 'launcher'" :ref="(el) => (pageRefs.launcher = el)" />
          <PersonalizationSettings
            v-else-if="activeTab === 'personalization'"
            :ref="(el) => (pageRefs.personalization = el)"
          />
          <AboutSettings v-else-if="activeTab === 'about'" :ref="(el) => (pageRefs.about = el)" />
        </Transition>

        <!-- 搜索无命中空态 -->
        <div
          v-if="emptyState"
          class="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-background text-[13px] text-hint-text"
        >
          <span class="text-[30px] text-muted-text">🔍</span>
          <span>没有找到相关设置，换个关键词试试喵~</span>
        </div>
      </div>
    </div>
  </section>
</template>

<script setup>
/*
 * Hub —— 设置中心导航壳（还原 SettingsHubPage）：
 * 顶栏（标题 + 胶囊搜索条）+ 左侧 MD3 导航项（胶囊高亮选中态）+ 右侧内容区。
 * 换装为 shadcn/ui + Tailwind：Input 搜索条（rounded-full 胶囊）、导航 hover/选中用
 * bg-accent/bg-muted 弱底、命中数用 Badge；页内过渡（transition name=page）保留。
 * 搜索跨「启动器设置 / 个性化」两个可索引页过滤卡片（原版含游戏设置页，此处已并入启动器页），
 * 命中数显示在导航项徽章上；当前页无命中时自动跳到第一个有结果的页，全无命中显示空态。
 * 注：web 端路由独立进入子页（/settings/launcher 等），本壳为 /settings 主入口。
 */
import { computed, ref, watch } from 'vue';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import LauncherSettings from './Launcher.vue';
import PersonalizationSettings from './Personalization.vue';
import AboutSettings from './About.vue';

const tabs = [
  { key: 'launcher', icon: '⚙', title: '启动器设置', desc: '实例、目录、Java 与下载' },
  { key: 'personalization', icon: '🎨', title: '个性化', desc: '主题、背景与工作区布局' },
  { key: 'about', icon: 'ℹ', title: '关于', desc: '团队、依赖与项目信息' },
];

const activeTab = ref('launcher');
const searchQuery = ref('');
const counts = ref({ launcher: -1, personalization: -1, about: -1 });
const pageRefs = { launcher: null, personalization: null, about: null };

const searching = computed(() => searchQuery.value.trim().length > 0);
const emptyState = computed(() => {
  if (!searching.value || activeTab.value === 'about') return false;
  return (counts.value[activeTab.value] ?? -1) === 0;
});

function switchTab(key) { activeTab.value = key; }

function applySearch() {
  const q = searchQuery.value.trim();
  const searchable = ['launcher', 'personalization'];
  if (!q) {
    counts.value = { launcher: -1, personalization: -1, about: -1 };
    return;
  }
  for (const key of searchable) {
    const page = pageRefs[key];
    counts.value[key] = page && typeof page.applySearchFilter === 'function'
      ? page.applySearchFilter(q)
      : -1;
  }
  // 当前页无命中而其他页有 → 自动跳到第一个有结果的页
  if (activeTab.value !== 'about'
    && (counts.value[activeTab.value] ?? -1) === 0
    && searchable.some((k) => counts.value[k] > 0)) {
    activeTab.value = searchable.find((k) => counts.value[k] > 0);
  }
}

watch(searchQuery, applySearch);
watch(activeTab, () => {
  if (searching.value) {
    // 切页后（组件挂载完成）对当前页重新应用过滤
    requestAnimationFrame(applySearch);
  }
});
</script>
