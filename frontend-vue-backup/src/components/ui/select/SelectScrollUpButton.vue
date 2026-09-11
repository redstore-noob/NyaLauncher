<script setup>
import { SelectScrollUpButton } from 'reka-ui';
import { ChevronUp } from '@lucide/vue';
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <SelectScrollUpButton :class="cn('flex items-center justify-center py-1 text-subtext-text', props.class)">
    <ChevronUp :size="14" />
  </SelectScrollUpButton>
</template>
