<script setup>
import { SelectScrollDownButton } from 'reka-ui';
import { ChevronDown } from '@lucide/vue';
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <SelectScrollDownButton :class="cn('flex items-center justify-center py-1 text-subtext-text', props.class)">
    <ChevronDown :size="14" />
  </SelectScrollDownButton>
</template>
