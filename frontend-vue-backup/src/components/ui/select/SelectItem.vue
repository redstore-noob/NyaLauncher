<script setup>
import { computed } from 'vue';
import { SelectItem, SelectItemText, SelectItemIndicator } from 'reka-ui';
import { Check } from '@lucide/vue';
import { cn } from '@/lib/utils';

const props = defineProps({
  value: { type: null, required: true },
  disabled: { type: Boolean, default: undefined },
  class: { type: null, default: '' },
});
const delegatedProps = computed(() => {
  const { class: _, ...delegated } = props;
  return delegated;
});
</script>
<template>
  <SelectItem
    v-bind="delegatedProps"
    :class="cn(
      'relative flex w-full cursor-pointer select-none items-center rounded-md py-1.5 pl-7 pr-2 text-[13px] outline-none',
      'focus:bg-accent focus:text-accent-foreground data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
      props.class,
    )"
  >
    <span class="absolute left-2 flex size-4 items-center justify-center">
      <SelectItemIndicator>
        <Check :size="14" class="text-primary" />
      </SelectItemIndicator>
    </span>
    <SelectItemText><slot /></SelectItemText>
  </SelectItem>
</template>
