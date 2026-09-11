<script setup>
import { computed } from 'vue';
import { DropdownMenuItem, useForwardPropsEmits } from 'reka-ui';
import { cn } from '@/lib/utils';

const props = defineProps({
  class: { type: null, default: '' },
  value: { type: String, default: undefined },
  disabled: { type: Boolean, default: undefined },
  inset: { type: Boolean, default: false },
});
const emits = defineEmits(['select']);
const delegatedProps = computed(() => {
  const { class: _, inset: __, ...delegated } = props;
  return delegated;
});
const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>
<template>
  <DropdownMenuItem
    v-bind="forwarded"
    :class="cn(
      'relative flex cursor-pointer select-none items-center gap-2 rounded-md px-2 py-1.5 text-[13px] outline-none',
      'transition-colors focus:bg-accent focus:text-accent-foreground data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
      inset && 'pl-8',
      props.class,
    )"
  ><slot /></DropdownMenuItem>
</template>
