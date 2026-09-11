<script setup>
import { computed } from 'vue';
import { SliderRoot, SliderTrack, SliderRange, SliderThumb, useForwardPropsEmits } from 'reka-ui';
import { cn } from '@/lib/utils';

const props = defineProps({
  defaultValue: { type: null, default: undefined },
  min: { type: Number, default: 0 },
  max: { type: Number, default: 100 },
  step: { type: Number, default: 1 },
  disabled: { type: Boolean, default: undefined },
  orientation: { type: String, default: 'horizontal' },
  class: { type: null, default: '' },
  thumbClass: { type: null, default: '' },
});
const emits = defineEmits(['update:modelValue', 'valueCommit']);
const delegatedProps = computed(() => {
  const { class: _, thumbClass: __, ...delegated } = props;
  return delegated;
});
const forwarded = useForwardPropsEmits(delegatedProps, emits);
const model = defineModel({ type: [Number, Array], default: 0 });
const thumbCount = computed(() => (Array.isArray(model.value) ? model.value.length : 1));
</script>
<template>
  <SliderRoot v-bind="forwarded" v-model="model" :class="cn('relative flex w-full touch-none select-none items-center', props.class)">
    <SliderTrack class="relative h-1.5 w-full grow overflow-hidden rounded-full bg-muted">
      <SliderRange class="absolute h-full bg-primary" />
    </SliderTrack>
    <SliderThumb
      v-for="i in thumbCount"
      :key="i"
      :class="cn(
        'block size-4 rounded-full border-2 border-primary bg-background shadow transition-transform',
        'focus-visible:outline-none focus-visible:ring-[2px] focus-visible:ring-ring/40',
        'disabled:pointer-events-none disabled:opacity-50',
        props.thumbClass,
      )"
    />
  </SliderRoot>
</template>
