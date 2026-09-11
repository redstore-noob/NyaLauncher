<script setup>
import { computed } from 'vue';
import { ProgressRoot, ProgressIndicator, useForwardProps } from 'reka-ui';
import { cn } from '@/lib/utils';

const props = defineProps({
  modelValue: { type: Number, default: 0 },
  max: { type: Number, default: 100 },
  class: { type: null, default: '' },
  indicatorClass: { type: null, default: '' },
});
const delegatedProps = useForwardProps(computed(() => {
  const { class: _, indicatorClass: __, ...delegated } = props;
  return delegated;
}));
</script>
<template>
  <ProgressRoot v-bind="delegatedProps" :class="cn('relative h-2 w-full overflow-hidden rounded-full bg-muted', props.class)">
    <ProgressIndicator
      :class="cn('h-full w-full flex-1 bg-primary transition-transform duration-300 ease-[var(--ease-emphasized-decelerate)]', props.indicatorClass)"
      :style="`transform: translateX(-${100 - (modelValue / max) * 100}%);`"
    />
  </ProgressRoot>
</template>
