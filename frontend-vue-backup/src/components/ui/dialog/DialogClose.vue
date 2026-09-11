<script setup>
import { DialogClose } from 'reka-ui';
const props = defineProps({ asChild: { type: Boolean, default: false }, class: { type: null, default: '' } });
</script>
<template>
  <DialogClose :as-child="asChild" :class="props.class"><slot /></DialogClose>
</template>
