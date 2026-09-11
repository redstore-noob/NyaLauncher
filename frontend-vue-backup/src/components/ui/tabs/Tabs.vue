<script setup>
import { TabsRoot, useForwardPropsEmits } from 'reka-ui';

const props = defineProps({
  modelValue: { type: null, default: undefined },
  defaultValue: { type: null, default: undefined },
  orientation: { type: String, default: 'horizontal' },
  activationMode: { type: String, default: 'automatic' },
  disabled: { type: Boolean, default: undefined },
});
const emits = defineEmits(['update:modelValue']);
const forwarded = useForwardPropsEmits(props, emits);
</script>
<template>
  <TabsRoot v-bind="forwarded"><slot /></TabsRoot>
</template>
