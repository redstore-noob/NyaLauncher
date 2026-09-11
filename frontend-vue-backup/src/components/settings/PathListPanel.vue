<template>
  <div class="flex min-w-0 flex-col gap-2">
    <div class="flex flex-col gap-1">
      <span class="text-[14px] font-semibold text-secondary-text">{{ label }}</span>
      <span class="text-[11px] leading-relaxed text-hint-text">{{ headHint }}</span>
    </div>
    <div class="grid grid-cols-[1fr_200px] items-start gap-3">
      <ul class="m-0 max-h-[190px] min-h-[120px] list-none overflow-y-auto rounded-md bg-muted p-2">
        <li
          v-for="(item, i) in items"
          :key="item.text + i"
          class="grid cursor-pointer grid-cols-[auto_1fr] items-center gap-2 rounded-sm px-1 py-1 transition-colors duration-150"
          :class="i === selectedIndex
            ? 'bg-accent ring-1 ring-medium-border'
            : 'hover:bg-accent/60'"
          @click="$emit('update:selectedIndex', i)"
        >
          <Badge
            :variant="item.badgeHighlight ? 'default' : 'secondary'"
            class="whitespace-nowrap rounded-[5px] px-2 py-0.5 text-[10px] font-semibold"
            :class="item.badgeHighlight ? 'bg-primary text-primary-foreground' : ''"
          >
            {{ item.badgeText }}
          </Badge>
          <span class="overflow-hidden text-ellipsis whitespace-nowrap text-[11px] text-primary-text" :title="item.text">
            {{ item.text }}
          </span>
        </li>
        <li v-if="items.length === 0" class="cursor-default px-1 py-1 text-[11px] text-hint-text">{{ emptyText }}</li>
      </ul>
      <div class="flex flex-col gap-2">
        <slot />
      </div>
    </div>
    <div v-if="$slots.hint" class="text-[11px] leading-relaxed text-hint-text">
      <slot name="hint" />
    </div>
  </div>
</template>

<script setup>
/*
 * PathListPanel —— 路径/Java 列表管理面板（对应原版 ListBox + 右侧竖排按钮 + 底部提示）。
 * 换装：shadcn Badge 做条目徽章（badgeHighlight=true 时 bg-primary 强调底白字），
 * 列表 hover/选中弱底用 bg-accent 工具类。
 * 右侧按钮经默认插槽传入；底部动态提示经 hint 插槽。
 * 对外 props/emits 接口保持不变。
 */
import { Badge } from '@/components/ui/badge';

defineProps({
  label: { type: String, required: true },
  headHint: { type: String, default: '' },
  items: { type: Array, default: () => [] }, // [{ badgeText, badgeHighlight, text }]
  selectedIndex: { type: Number, default: -1 },
  emptyText: { type: String, default: '（空）' },
});
defineEmits(['update:selectedIndex']);
</script>
