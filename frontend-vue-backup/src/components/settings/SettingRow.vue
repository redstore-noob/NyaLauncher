<template>
  <div
    class="grid grid-cols-[1fr_auto] items-center gap-4 rounded-lg border border-subtle-border bg-muted px-4 py-3"
    :data-title="title"
    :data-hint="hint || ''"
  >
    <div class="flex min-w-0 flex-col gap-1">
      <span class="text-[14px] font-semibold text-secondary-text">{{ title }}</span>
      <span v-if="hint" class="text-[11px] leading-relaxed text-hint-text">{{ hint }}</span>
      <slot name="extra-hint" />
    </div>
    <div class="flex shrink-0 items-center gap-2">
      <slot />
    </div>
  </div>
</template>

<script setup>
/*
 * SettingRow —— 设置行（shadcn 质感：bg-muted 控件底子卡 + subtle-border + rounded-lg）。
 * 右侧控件经默认插槽传入（NyaToggle / NyaSelect / 按钮组等）。
 * extra-hint 插槽放行内动态提示（如内存决策文案）。
 */
defineProps({
  title: { type: String, required: true },
  hint: { type: String, default: '' },
});
</script>
