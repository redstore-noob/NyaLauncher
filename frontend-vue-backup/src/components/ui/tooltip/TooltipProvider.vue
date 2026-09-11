<script setup>
import { TooltipProvider } from 'reka-ui';
const props = defineProps({ delayDuration: { type: Number, default: 300 }, skipDelayDuration: { type: Number, default: 100 } });
</script>
<template>
  <TooltipProvider :delay-duration="delayDuration" :skip-delay-duration="skipDelayDuration"><slot /></TooltipProvider>
</template>
