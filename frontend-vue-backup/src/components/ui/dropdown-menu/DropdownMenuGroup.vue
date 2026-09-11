<script setup>
import { DropdownMenuGroup } from 'reka-ui';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <DropdownMenuGroup :class="props.class"><slot /></DropdownMenuGroup>
</template>
