<script setup>
import { computed } from 'vue';
import { DialogPortal, DialogOverlay, DialogContent, DialogClose, useForwardPropsEmits } from 'reka-ui';
import { X } from '@lucide/vue';
import { cn } from '@/lib/utils';

const props = defineProps({
  class: { type: null, default: '' },
  showCloseButton: { type: Boolean, default: true },
});
const emits = defineEmits([
  'update:open',
  'escapeKeyDown',
  'pointerDownOutside',
  'interactOutside',
  'openAutoFocus',
  'closeAutoFocus',
]);
const delegatedProps = computed(() => {
  const { class: _, ...delegated } = props;
  return delegated;
});
const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>
<template>
  <DialogPortal>
    <!-- 遮罩：nya-pop 进出场（220ms decelerate / 150ms accelerate，原版动画令牌） -->
    <DialogOverlay class="nya-pop fixed inset-0 z-50 bg-overlay" />
    <DialogContent
      v-bind="forwarded"
      :class="cn(
        'nya-pop fixed left-1/2 top-1/2 z-50 grid w-full max-w-lg -translate-x-1/2 -translate-y-1/2 gap-4',
        'rounded-xl border border-strong-border bg-popover p-6 text-popover-foreground shadow-2xl outline-none',
        props.class,
      )"
    >
      <slot />
      <DialogClose v-if="showCloseButton"
        class="absolute right-3 top-3 rounded-md p-1 text-subtext-text opacity-70 transition-opacity hover:opacity-100 focus:outline-none">
        <X :size="16" />
        <span class="sr-only">关闭</span>
      </DialogClose>
    </DialogContent>
  </DialogPortal>
</template>
