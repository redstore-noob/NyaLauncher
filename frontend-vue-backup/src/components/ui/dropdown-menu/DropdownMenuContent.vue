<script setup>
import { computed } from 'vue';
import { DropdownMenuPortal, DropdownMenuContent, useForwardPropsEmits } from 'reka-ui';
import { cn } from '@/lib/utils';

const props = defineProps({
  class: { type: null, default: '' },
  sideOffset: { type: Number, default: 6 },
  side: { type: String, default: 'bottom' },
  align: { type: String, default: 'start' },
  alignOffset: { type: Number, default: 0 },
});
const emits = defineEmits([
  'escapeKeyDown', 'pointerDownOutside', 'focusOutside', 'interactOutside', 'closeAutoFocus',
]);
const delegatedProps = computed(() => {
  const { class: _, ...delegated } = props;
  return delegated;
});
const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>
<template>
  <DropdownMenuPortal>
    <DropdownMenuContent
      v-bind="forwarded"
      :class="cn(
        'nya-pop z-50 min-w-[8rem] overflow-hidden rounded-lg border border-strong-border bg-popover p-1',
        'text-popover-foreground shadow-xl',
        props.class,
      )"
    ><slot /></DropdownMenuContent>
  </DropdownMenuPortal>
</template>
