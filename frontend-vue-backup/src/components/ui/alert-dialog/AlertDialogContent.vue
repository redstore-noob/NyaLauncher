<script setup>
import { AlertDialogPortal, AlertDialogOverlay, AlertDialogContent, useForwardPropsEmits } from 'reka-ui';
import { cn } from '@/lib/utils';

const props = defineProps({ class: { type: null, default: '' } });
const emits = defineEmits(['update:open']);
const forwarded = useForwardPropsEmits(props, emits);
</script>
<template>
  <AlertDialogPortal>
    <AlertDialogOverlay class="nya-pop fixed inset-0 z-50 bg-overlay" />
    <AlertDialogContent
      v-bind="forwarded"
      :class="cn(
        'nya-pop fixed left-1/2 top-1/2 z-50 grid w-full max-w-md -translate-x-1/2 -translate-y-1/2 gap-4',
        'rounded-xl border border-strong-border bg-popover p-6 text-popover-foreground shadow-2xl outline-none',
        props.class,
      )"
    >
      <slot />
    </AlertDialogContent>
  </AlertDialogPortal>
</template>
