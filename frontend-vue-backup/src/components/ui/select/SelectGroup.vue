<script setup>
import { SelectGroup } from 'reka-ui';
const props = defineProps({ class: { type: null, default: '' } });
</script>
<template>
  <SelectGroup :class="props.class"><slot /></SelectGroup>
</template>
