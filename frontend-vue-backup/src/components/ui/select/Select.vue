<script setup>
import { SelectRoot, useForwardPropsEmits } from 'reka-ui';

const props = defineProps({
  modelValue: { type: null, default: undefined },
  defaultValue: { type: null, default: undefined },
  open: { type: Boolean, default: undefined },
  defaultOpen: { type: Boolean, default: undefined },
  disabled: { type: Boolean, default: undefined },
  required: { type: Boolean, default: undefined },
  name: { type: String, default: undefined },
  multiple: { type: Boolean, default: undefined },
});
const emits = defineEmits(['update:modelValue', 'update:open']);
const forwarded = useForwardPropsEmits(props, emits);
</script>
<template>
  <SelectRoot v-bind="forwarded"><slot /></SelectRoot>
</template>
