<script setup>
import { SelectValue } from 'reka-ui';
const props = defineProps({ placeholder: { type: String, default: '' }, class: { type: null, default: '' } });
</script>
<template>
  <SelectValue :placeholder="placeholder" :class="props.class"><slot /></SelectValue>
</template>
