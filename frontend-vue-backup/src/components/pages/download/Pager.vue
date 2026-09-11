<template>
  <!-- 分页条（DownloadPage.axaml 上一页/下一页：ControlBg 容器 + PanelBg 按钮） -->
  <div class="flex items-center justify-between rounded-sm bg-muted px-4 py-2">
    <Button variant="secondary" size="sm" :disabled="page <= 1" @click="$emit('prev')">◀ 上一页</Button>
    <span class="text-[13px] text-hint-text">第 {{ page }} 页 / 共 {{ totalPages }} 页</span>
    <Button variant="secondary" size="sm" :disabled="page >= totalPages" @click="$emit('next')">下一页</Button>
  </div>
</template>

<script setup>
import { Button } from '@/components/ui/button'

defineProps({
  page: { type: Number, required: true },
  totalPages: { type: Number, required: true },
})
defineEmits(['prev', 'next'])
</script>
