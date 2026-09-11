<script setup>
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <div :class="cn('rounded-2xl border border-card-border bg-card text-card-foreground', props.class)">
    <slot />
  </div>
</template>
