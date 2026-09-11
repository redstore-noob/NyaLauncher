<template>
  <!-- 搜索栏（DownloadPage.axaml 各标签搜索框：ControlBg r10 + 放大镜 + 右侧筛选插槽） -->
  <div class="flex items-center gap-2 rounded-md bg-muted px-3 py-2">
    <Search :size="14" class="shrink-0 text-muted-text" />
    <input
      class="min-w-0 flex-1 border-none bg-transparent text-[13px] text-body-text outline-none placeholder:text-placeholder-text"
      type="text"
      :value="modelValue"
      :placeholder="placeholder"
      @input="$emit('update:modelValue', $event.target.value)"
    />
    <slot />
  </div>
</template>

<script setup>
import { Search } from '@lucide/vue'

defineProps({
  modelValue: { type: String, default: '' },
  placeholder: { type: String, default: '' },
})
defineEmits(['update:modelValue'])
</script>
