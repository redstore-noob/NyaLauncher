<script setup>
import { ScrollAreaRoot, ScrollAreaViewport } from 'reka-ui';
import { cn } from '@/lib/utils';
import ScrollBar from './ScrollBar.vue';

const props = defineProps({
  class: { type: null, default: '' },
  viewportClass: { type: null, default: '' },
  scrollbarClass: { type: null, default: '' },
  orientation: { type: String, default: 'vertical' },
});
</script>
<template>
  <ScrollAreaRoot :class="cn('relative overflow-hidden', props.class)">
    <ScrollAreaViewport
      :class="cn('h-full w-full rounded-[inherit] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden', props.viewportClass)"
    >
      <slot />
    </ScrollAreaViewport>
    <ScrollBar :orientation="orientation" :class="scrollbarClass" />
    <slot name="corner" />
  </ScrollAreaRoot>
</template>
