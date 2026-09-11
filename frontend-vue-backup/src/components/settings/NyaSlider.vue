<template>
  <div class="flex min-w-0 flex-col gap-2">
    <div v-if="label || showBadge" class="flex items-center justify-between gap-2">
      <span class="text-[14px] font-semibold text-secondary-text">{{ label }}</span>
      <span
        v-if="showBadge"
        class="rounded-sm bg-badge px-2 py-1 text-[13px] font-bold text-primary"
      >{{ badgeText }}</span>
    </div>
    <Slider
      :model-value="modelValue"
      :min="min"
      :max="max"
      :step="step"
      :disabled="disabled"
      class="cursor-pointer"
      @update:model-value="onInput"
    />
    <div v-if="$slots.default" class="text-[11px] leading-relaxed text-hint-text">
      <slot />
    </div>
  </div>
</template>

<script setup>
/*
 * NyaSlider —— 对应 Avalonia Slider（IsSnapToTickEnabled + TickPlacement=BottomRight）。
 * 内部实现换装为 shadcn Slider（reka-ui SliderRoot：bg-muted 轨道 + bg-primary 已选段 + 圆形 thumb）。
 * showBadge 显示右侧强调色数值徽章（对应原版 BadgeBg 圆角数值块）。
 * 对外 props/emits 接口保持不变：modelValue/min/max/step/disabled/label/showBadge/badgeText
 * → update:modelValue / change，默认插槽为补充说明。
 */
import { Slider } from '@/components/ui/slider';

const props = defineProps({
  modelValue: { type: Number, required: true },
  min: { type: Number, default: 0 },
  max: { type: Number, default: 100 },
  step: { type: Number, default: 1 },
  disabled: { type: Boolean, default: false },
  label: { type: String, default: '' },
  showBadge: { type: Boolean, default: false },
  badgeText: { type: [String, Number], default: '' },
});
const emit = defineEmits(['update:modelValue', 'change']);

function onInput(v) {
  const num = Array.isArray(v) ? v[0] : v;
  if (typeof num !== 'number' || Number.isNaN(num)) return;
  emit('update:modelValue', num);
  emit('change', num);
}
</script>
