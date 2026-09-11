<template>
  <Select
    :model-value="modelValue"
    :disabled="disabled"
    @update:model-value="onChange"
  >
    <SelectTrigger class="w-full max-w-[320px]">
      <SelectValue :placeholder="currentLabel" />
    </SelectTrigger>
    <SelectContent>
      <SelectItem v-for="opt in options" :key="opt.value" :value="opt.value">
        {{ opt.label }}
      </SelectItem>
    </SelectContent>
  </Select>
</template>

<script setup>
/*
 * NyaSelect —— 对应 Avalonia ComboBox。内部实现换装为 shadcn Select
 * （reka-ui SelectRoot，弹层自带 .nya-pop 进出场与 bg-accent 选中弱底）。
 * options: [{ value, label }]；v-model 绑定选中 value。
 * 对外 props/emits 接口保持不变：modelValue / options / disabled → update:modelValue / change。
 */
import { computed } from 'vue';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';

const props = defineProps({
  modelValue: { type: [String, Number], default: '' },
  options: { type: Array, default: () => [] },
  disabled: { type: Boolean, default: false },
});
const emit = defineEmits(['update:modelValue', 'change']);

const currentLabel = computed(() =>
  props.options.find((o) => o.value === props.modelValue)?.label ?? '');

function onChange(v) {
  emit('update:modelValue', v);
  emit('change', v);
}
</script>
