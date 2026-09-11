<script setup>
import { AlertDialogRoot, useForwardPropsEmits } from 'reka-ui';

const props = defineProps({
  open: { type: Boolean, default: undefined },
  defaultOpen: { type: Boolean, default: undefined },
});
const emits = defineEmits(['update:open']);
const forwarded = useForwardPropsEmits(props, emits);
</script>
<template>
  <AlertDialogRoot v-bind="forwarded"><slot /></AlertDialogRoot>
</template>
