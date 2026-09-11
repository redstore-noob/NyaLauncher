<script setup>
import { Separator } from 'reka-ui';
import { cn } from '@/lib/utils';

const props = defineProps({
  orientation: { type: String, default: 'horizontal' },
  decorative: { type: Boolean, default: true },
  class: { type: null, default: '' },
});
</script>
<template>
  <Separator
    :orientation="orientation"
    :decorative="decorative"
    :class="cn('shrink-0 bg-border data-[orientation=horizontal]:h-px data-[orientation=horizontal]:w-full data-[orientation=vertical]:h-full data-[orientation=vertical]:w-px', props.class)"
  />
</template>
