<script setup>
import { DropdownMenuSubTrigger } from 'reka-ui';
import { ChevronRight } from '@lucide/vue';
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' }, disabled: { type: Boolean, default: undefined } });
</script>
<template>
  <DropdownMenuSubTrigger
    :disabled="disabled"
    :class="cn(
      'flex cursor-pointer select-none items-center rounded-md px-2 py-1.5 text-[13px] outline-none',
      'focus:bg-accent data-[state=open]:bg-accent',
      props.class,
    )"
  >
    <slot />
    <ChevronRight :size="14" class="ml-auto text-subtext-text" />
  </DropdownMenuSubTrigger>
</template>

