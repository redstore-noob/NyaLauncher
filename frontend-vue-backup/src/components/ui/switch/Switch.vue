<script setup>
import { SwitchRoot, SwitchThumb, useForwardProps } from 'reka-ui';
import { cn } from '@/lib/utils';

const props = defineProps({
  disabled: { type: Boolean, default: undefined },
  class: { type: null, default: '' },
  thumbClass: { type: null, default: '' },
});
const delegatedProps = useForwardProps(computed(() => {
  const { class: _, thumbClass: __, ...delegated } = props;
  return delegated;
}));
const model = defineModel({ type: Boolean, default: false });
import { computed } from 'vue';
</script>
<template>
  <SwitchRoot
    v-bind="delegatedProps"
    v-model="model"
    :class="cn(
      'peer inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent',
      'transition-colors duration-150 focus-visible:outline-none focus-visible:ring-[2px] focus-visible:ring-ring/40',
      'disabled:cursor-not-allowed disabled:opacity-50',
      'data-[state=checked]:bg-primary data-[state=unchecked]:bg-muted',
      props.class,
    )"
  >
    <SwitchThumb
      :class="cn(
        'pointer-events-none block size-4 rounded-full bg-background shadow transition-transform duration-150',
        'data-[state=checked]:translate-x-4 data-[state=unchecked]:translate-x-0',
        props.thumbClass,
      )"
    />
  </SwitchRoot>
</template>
