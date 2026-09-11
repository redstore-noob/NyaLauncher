<script setup>
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <div :class="cn('text-[11px] text-muted-foreground', props.class)"><slot /></div>
</template>
