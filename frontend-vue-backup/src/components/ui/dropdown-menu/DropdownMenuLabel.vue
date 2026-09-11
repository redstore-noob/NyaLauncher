<script setup>
import { DropdownMenuLabel } from 'reka-ui';
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' }, inset: { type: Boolean, default: false } });
</script>
<template>
  <DropdownMenuLabel :class="cn('px-2 py-1.5 text-[11px] font-semibold text-muted-foreground', inset && 'pl-8', props.class)"><slot /></DropdownMenuLabel>
</template>
