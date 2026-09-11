<script setup>
import { Primitive } from 'reka-ui';
import { cn } from '@/lib/utils';
import { badgeVariants } from '.';

const props = defineProps({
  variant: { type: String, default: 'default' },
  class: { type: null, default: '' },
  asChild: { type: Boolean, default: false },
  as: { type: null, default: 'span' },
});
</script>

<template>
  <Primitive :as="as" :as-child="asChild" :class="cn(badgeVariants({ variant }), props.class)">
    <slot />
  </Primitive>
</template>
