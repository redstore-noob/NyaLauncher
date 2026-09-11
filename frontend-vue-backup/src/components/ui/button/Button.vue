<script setup>
import { ref } from 'vue';
import { Primitive } from 'reka-ui';
import { cn } from '@/lib/utils';
import { buttonVariants } from '.';

const props = defineProps({
  variant: { type: String, default: 'default' },
  size: { type: String, default: 'default' },
  class: { type: null, default: '' },
  asChild: { type: Boolean, default: false },
  as: { type: null, default: 'button' },
  type: { type: String, default: 'button' },
  disabled: { type: Boolean, default: false },
});

const bouncing = ref(false);
let bounceTimer = null;

// 松开后的三段回弹（nya-bounce：0.97→1.06→0.98→1，300ms，AnimationHelper.cs BounceBehavior）
function handleClick() {
  if (props.disabled) return;
  bouncing.value = false;
  requestAnimationFrame(() => {
    bouncing.value = true;
    clearTimeout(bounceTimer);
    bounceTimer = setTimeout(() => { bouncing.value = false; }, 320);
  });
}
</script>

<template>
  <Primitive
    :as="as"
    :as-child="asChild"
    :type="asChild ? undefined : type"
    :disabled="disabled || undefined"
    :class="cn(buttonVariants({ variant, size }), 'btn-anim', bouncing && 'btn-bounce', props.class)"
    @click="handleClick"
  >
    <slot />
  </Primitive>
</template>
