<script setup>
import { AlertDialogAction } from 'reka-ui';
import { cn } from '@/lib/utils';
import { buttonVariants } from '../button';

const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <AlertDialogAction :class="cn(buttonVariants(), props.class)"><slot /></AlertDialogAction>
</template>
