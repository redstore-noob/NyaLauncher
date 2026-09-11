<script setup>
import { TooltipTrigger } from 'reka-ui';
const props = defineProps({ asChild: { type: Boolean, default: false }, class: { type: null, default: '' } });
</script>
<template>
  <TooltipTrigger :as-child="asChild" :class="props.class"><slot /></TooltipTrigger>
</template>
