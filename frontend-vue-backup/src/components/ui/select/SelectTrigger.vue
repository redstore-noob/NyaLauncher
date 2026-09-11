<script setup>
import { SelectTrigger, SelectIcon } from 'reka-ui';
import { ChevronDown } from '@lucide/vue';
import { cn } from '@/lib/utils';

const props = defineProps({ class: { type: null, default: '' }, disabled: { type: Boolean, default: undefined } });
</script>
<template>
  <SelectTrigger
    :disabled="disabled"
    :class="cn(
      'flex h-[34px] w-full items-center justify-between gap-2 rounded-md border border-input bg-secondary px-3',
      'text-[13px] text-foreground outline-none transition-colors',
      'focus-visible:border-ring focus-visible:ring-[2px] focus-visible:ring-ring/40',
      'disabled:cursor-not-allowed disabled:opacity-50',
      'data-[placeholder]:text-placeholder-text',
      props.class,
    )"
  >
    <slot />
    <SelectIcon as-child>
      <ChevronDown :size="14" class="shrink-0 text-subtext-text" />
    </SelectIcon>
  </SelectTrigger>
</template>
