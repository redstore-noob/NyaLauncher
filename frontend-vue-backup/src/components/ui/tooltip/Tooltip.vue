<script setup>
import { TooltipRoot, useForwardPropsEmits } from 'reka-ui';

const props = defineProps({
  open: { type: Boolean, default: undefined },
  defaultOpen: { type: Boolean, default: undefined },
  delayDuration: { type: Number, default: undefined },
  disableHoverableContent: { type: Boolean, default: undefined },
  disableClosingTrigger: { type: Boolean, default: undefined },
  disabled: { type: Boolean, default: undefined },
});
const emits = defineEmits(['update:open']);
const forwarded = useForwardPropsEmits(props, emits);
</script>
<template>
  <TooltipRoot v-bind="forwarded"><slot /></TooltipRoot>
</template>
