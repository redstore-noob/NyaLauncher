<script setup>
import { DialogTitle } from 'reka-ui';
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <DialogTitle :class="cn('text-[15px] font-semibold leading-none text-foreground', props.class)"><slot /></DialogTitle>
</template>
