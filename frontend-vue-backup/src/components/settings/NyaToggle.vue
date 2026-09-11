<template>
  <label class="inline-flex cursor-pointer items-center gap-2 select-none" :class="{ 'cursor-not-allowed opacity-50': disabled }">
    <span class="text-[13px] text-secondary-text">{{ modelValue ? '开' : '关' }}</span>
    <Switch
      :model-value="modelValue"
      :disabled="disabled"
      @update:model-value="toggle"
    />
  </label>
</template>

<script setup>
/*
 * NyaToggle —— 对应 Avalonia ToggleSwitch（OnContent="开" OffContent="关"）。
 * 内部实现换装为 shadcn Switch（reka-ui SwitchRoot，选中态 bg-primary，thumb 位移过渡）。
 * 对外 props/emits 接口保持不变：modelValue / disabled → update:modelValue / change。
 */
import { Switch } from '@/components/ui/switch';

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
});
const emit = defineEmits(['update:modelValue', 'change']);

function toggle(v) {
  if (props.disabled) return;
  emit('update:modelValue', v);
  emit('change', v);
}
</script>
