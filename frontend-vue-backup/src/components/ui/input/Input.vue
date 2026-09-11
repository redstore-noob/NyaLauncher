<script setup>
import { cn } from '@/lib/utils';

const props = defineProps({ class: { type: null, default: '' } });
const model = defineModel({ type: [String, Number], default: '' });
</script>
<template>
  <input
    v-model="model"
    :class="cn(
      'flex h-[34px] w-full rounded-md border border-input bg-background px-3 py-1 text-[13px] text-foreground',
      'placeholder:text-placeholder-text transition-[border-color,box-shadow] duration-150',
      'focus-visible:border-ring focus-visible:outline-none focus-visible:ring-[2px] focus-visible:ring-ring/40',
      'disabled:cursor-not-allowed disabled:opacity-50',
      props.class,
    )"
  >
</template>
