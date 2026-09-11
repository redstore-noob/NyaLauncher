<script setup>
import { DropdownMenuRoot, useForwardPropsEmits } from 'reka-ui';

const props = defineProps({
  open: { type: Boolean, default: undefined },
  defaultOpen: { type: Boolean, default: undefined },
  modal: { type: Boolean, default: undefined },
  dir: { type: String, default: undefined },
});
const emits = defineEmits(['update:open']);
const forwarded = useForwardPropsEmits(props, emits);
</script>
<template>
  <DropdownMenuRoot v-bind="forwarded"><slot /></DropdownMenuRoot>
</template>
