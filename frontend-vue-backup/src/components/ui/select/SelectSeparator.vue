<script setup>
import { SelectSeparator } from 'reka-ui';
import { cn } from '@/lib/utils';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <SelectSeparator :class="cn('-mx-1 my-1 h-px bg-border', props.class)" />
</template>
