<script setup>
import { DropdownMenuSub } from 'reka-ui';
const props = defineProps({ open: { type: Boolean, default: undefined }, defaultOpen: { type: Boolean, default: undefined } });
</script>
<template>
  <DropdownMenuSub><slot /></DropdownMenuSub>
</template>
