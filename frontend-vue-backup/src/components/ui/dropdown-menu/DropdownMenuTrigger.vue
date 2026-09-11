<script setup>
import { DropdownMenuTrigger } from 'reka-ui';
const props = defineProps({ asChild: { type: Boolean, default: false }, class: { type: null, default: '' }, disabled: { type: Boolean, default: undefined } });
</script>
<template>
  <DropdownMenuTrigger :as-child="asChild" :disabled="disabled" :class="props.class"><slot /></DropdownMenuTrigger>
</template>
