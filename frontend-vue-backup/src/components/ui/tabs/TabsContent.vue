<script setup>
import { TabsContent } from 'reka-ui';
import { cn } from '@/lib/utils';
const props = defineProps({ value: { type: null, required: true }, class: { type: null, default: '' } });
</script>
<template>
  <TabsContent :value="value" :class="cn('mt-2 outline-none focus-visible:ring-[2px] focus-visible:ring-ring/40', props.class)"><slot /></TabsContent>
</template>
