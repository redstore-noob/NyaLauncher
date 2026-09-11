<script setup>
import { computed } from 'vue';
import {
  SelectPortal, SelectContent, SelectViewport, SelectArrow,
  useForwardPropsEmits,
} from 'reka-ui';
import { cn } from '@/lib/utils';
import SelectScrollUpButton from './SelectScrollUpButton.vue';
import SelectScrollDownButton from './SelectScrollDownButton.vue';

const props = defineProps({
  class: { type: null, default: '' },
  position: { type: String, default: 'popper' },
  sideOffset: { type: Number, default: 6 },
  align: { type: String, default: 'start' },
});
const emits = defineEmits(['closeAutoFocus']);
const delegatedProps = computed(() => {
  const { class: _, ...delegated } = props;
  return delegated;
});
const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>
<template>
  <SelectPortal>
    <SelectContent
      v-bind="{ ...forwarded, class: undefined }"
      :class="cn(
        'nya-pop relative z-50 max-h-96 min-w-[8rem] overflow-hidden rounded-lg border border-strong-border',
        'bg-popover text-popover-foreground shadow-xl',
        position === 'popper' && 'data-[side=bottom]:translate-y-1 data-[side=top]:-translate-y-1',
        props.class,
      )"
    >
      <SelectScrollUpButton />
      <SelectViewport :class="cn('p-1', position === 'popper' && 'h-[var(--reka-select-trigger-height)] w-full min-w-[var(--reka-select-trigger-width)]')">
        <slot />
      </SelectViewport>
      <SelectScrollDownButton />
      <SelectArrow v-if="false" />
    </SelectContent>
  </SelectPortal>
</template>
