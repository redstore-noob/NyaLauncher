<script setup>
import { computed } from 'vue';
import { TooltipPortal, TooltipContent, useForwardPropsEmits } from 'reka-ui';
import { cn } from '@/lib/utils';

const props = defineProps({
  class: { type: null, default: '' },
  side: { type: String, default: 'top' },
  sideOffset: { type: Number, default: 6 },
  align: { type: String, default: 'center' },
});
const emits = defineEmits(['escapeKeyDown', 'pointerDownOutside']);
const delegatedProps = computed(() => {
  const { class: _, ...delegated } = props;
  return delegated;
});
const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>
<template>
  <TooltipPortal>
    <TooltipContent
      v-bind="forwarded"
      :class="cn(
        'nya-pop z-50 overflow-hidden rounded-md border border-strong-border bg-popover px-3 py-1.5',
        'text-[11px] text-popover-foreground shadow-md',
        props.class,
      )"
    ><slot /></TooltipContent>
  </TooltipPortal>
</template>
