<script setup>
import { SelectLabel } from 'reka-ui';
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <SelectLabel :class="cn('px-2 py-1.5 text-[11px] font-semibold text-muted-foreground', props.class)"><slot /></SelectLabel>
</template>
