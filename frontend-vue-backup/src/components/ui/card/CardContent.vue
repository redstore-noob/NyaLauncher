<script setup>
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <div :class="cn('p-5 pt-0', props.class)"><slot /></div>
</template>
