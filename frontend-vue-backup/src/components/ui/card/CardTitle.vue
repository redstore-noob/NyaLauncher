<script setup>
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <div :class="cn('text-[15px] font-semibold leading-none text-foreground', props.class)"><slot /></div>
</template>
