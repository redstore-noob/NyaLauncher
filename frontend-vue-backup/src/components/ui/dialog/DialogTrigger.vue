<script setup>
import { DialogTrigger } from 'reka-ui';
const props = defineProps({ asChild: { type: Boolean, default: false }, class: { type: null, default: '' } });
</script>
<template>
  <DialogTrigger :as-child="asChild" :class="props.class"><slot /></DialogTrigger>
</template>
