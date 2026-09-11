<script setup>
import { TabsTrigger } from 'reka-ui';
import { cn } from '@/lib/utils';
const props = defineProps({ value: { type: null, required: true }, disabled: { type: Boolean, default: undefined }, class: { type: null, default: '' } });
</script>
<template>
  <TabsTrigger
    :value="value"
    :disabled="disabled"
    :class="cn(
      'inline-flex items-center justify-center whitespace-nowrap rounded-md px-3 py-1 text-[13px] font-medium outline-none',
      'transition-all focus-visible:ring-[2px] focus-visible:ring-ring/40 disabled:pointer-events-none disabled:opacity-50',
      'data-[state=active]:bg-card data-[state=active]:text-foreground data-[state=active]:shadow-sm',
      props.class,
    )"
  ><slot /></TabsTrigger>
</template>
