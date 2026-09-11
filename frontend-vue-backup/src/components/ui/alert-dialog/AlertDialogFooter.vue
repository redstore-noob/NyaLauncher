<script setup>
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <div :class="cn('flex flex-col-reverse gap-2 sm:flex-row sm:justify-end', props.class)"><slot /></div>
</template>
