<script setup>
// Sonner toast（vue-sonner 封装）：主题/配色跟随 NyaLauncher 主题家族与明暗模式。
import { Toaster } from 'vue-sonner';
import { computed } from 'vue';
import { themeStore } from '@/store/theme';

const mode = computed(() => (themeStore.resolvedMode() === 'light' ? 'light' : 'dark'));
</script>
<template>
  <Toaster :theme="mode" position="bottom-right" :duration="3500" close-button rich-colors />
</template>
