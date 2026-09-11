<template>
  <!-- 主窗口工作区：左 2/3 组件画布 + 右 1/3 游戏启动区（MainWindow.axaml MainContentHost 2*,*） -->
  <div class="grid h-full min-h-0 grid-cols-[2fr_1fr]">
    <ComponentCanvas class="min-w-0" />
    <LaunchPanel class="min-w-0" />
  </div>
</template>

<script setup>
import ComponentCanvas from '../components/pages/home/ComponentCanvas.vue'
import LaunchPanel from '../components/pages/home/LaunchPanel.vue'
</script>
