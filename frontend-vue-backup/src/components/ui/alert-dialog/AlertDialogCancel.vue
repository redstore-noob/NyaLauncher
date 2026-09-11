<script setup>
import { AlertDialogCancel } from 'reka-ui';
import { cn } from '@/lib/utils';
import { buttonVariants } from '../button';

const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <AlertDialogCancel :class="cn(buttonVariants({ variant: 'outline' }), props.class)"><slot /></AlertDialogCancel>
</template>
