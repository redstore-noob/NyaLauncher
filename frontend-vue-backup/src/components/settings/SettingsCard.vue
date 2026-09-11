<template>
  <section
    ref="cardEl"
    class="rounded-2xl border border-card-border bg-card text-card-foreground shadow-sm transition-[transform,box-shadow] duration-200 ease-[var(--ease-emphasized)] hover:shadow-md"
    :data-title="title"
  >
    <div class="flex flex-col gap-5 p-6">
      <div class="grid grid-cols-[auto_1fr_auto] items-center gap-3">
        <div class="flex size-9 items-center justify-center rounded-lg bg-accent text-[17px] text-primary" aria-hidden="true">
          {{ icon }}
        </div>
        <div class="flex flex-col">
          <h3 class="m-0 text-[17px] font-semibold text-primary-text">{{ title }}</h3>
          <p v-if="subtitle" class="mt-0.5 mb-0 text-[11px] text-hint-text">{{ subtitle }}</p>
        </div>
        <div class="flex items-center gap-2">
          <slot name="actions" />
        </div>
      </div>
      <slot />
    </div>
  </section>
</template>

<script setup>
/*
 * SettingsCard —— 设置页大卡（shadcn 质感：bg-card + card-border + rounded-2xl + shadow）。
 * 头部为 icon-chip 图标章（bg-accent 弱底）+ 卡片标题/副标题；actions 插槽放右上角按钮。
 * 卡片标题等文本参与设置页搜索（Hub 会调子页的 applySearchFilter，本组件用 data-title 标记）。
 * 卡片自带 hover 阴影过渡（原版卡片 hover 动效保留，transform 留给页面滚动聚焦驱动）。
 */
import { ref } from 'vue';

defineProps({
  icon: { type: String, default: '' },
  title: { type: String, required: true },
  subtitle: { type: String, default: '' },
});

const cardEl = ref(null);
// 滚动聚焦微缩放由页面统一驱动（对应 OnSettingsScrollChanged 的 1.0→0.97）

defineExpose({ el: cardEl });
</script>
