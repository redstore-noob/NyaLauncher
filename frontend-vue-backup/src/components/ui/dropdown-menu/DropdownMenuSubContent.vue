<script setup>
import { computed } from 'vue';
import { DropdownMenuPortal, DropdownMenuSubContent, useForwardPropsEmits } from 'reka-ui';
import { cn } from '@/lib/utils';

const props = defineProps({
  class: { type: null, default: '' },
  sideOffset: { type: Number, default: 4 },
});
const emits = defineEmits(['escapeKeyDown', 'pointerDownOutside', 'focusOutside', 'interactOutside']);
const delegatedProps = computed(() => {
  const { class: _, ...delegated } = props;
  return delegated;
});
const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>
<template>
  <DropdownMenuPortal>
    <DropdownMenuSubContent
      v-bind="forwarded"
      :class="cn(
        'nya-pop z-50 min-w-[8rem] overflow-hidden rounded-lg border border-strong-border bg-popover p-1 text-popover-foreground shadow-xl',
        props.class,
      )"
    ><slot /></DropdownMenuSubContent>
  </DropdownMenuPortal>
</template>
