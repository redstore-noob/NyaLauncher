<script setup>
import { AlertDialogTrigger } from 'reka-ui';
const props = defineProps({ asChild: { type: Boolean, default: false }, class: { type: null, default: '' } });
</script>
<template>
  <AlertDialogTrigger :as-child="asChild" :class="props.class"><slot /></AlertDialogTrigger>
</template>
