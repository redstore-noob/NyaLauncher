<template>
  <!-- Material 风提示对话框（NyaPromptHost.axaml 移植）——重构为 ui/ Dialog 基座 -->
  <Dialog :open="overlayState.dialogVisible && !!overlayState.dialog">
    <DialogContent
      class="max-w-[460px] min-w-[320px] gap-0 rounded-2xl p-6 text-center"
      :show-close-button="false"
      @escape-key-down.prevent
      @pointer-down-outside.prevent
      @interact-outside.prevent
      @update:open="() => {}"
    >
      <span class="mb-3 flex justify-center" :style="{ color: mapped.color }">
        <Icon :name="mapped.icon" :size="28" />
      </span>

      <DialogHeader class="space-y-0 text-center sm:text-center">
        <DialogTitle class="text-lg font-semibold break-words">
          {{ overlayState.dialog?.title }}
        </DialogTitle>
        <DialogDescription
          v-if="overlayState.dialog?.message"
          class="mt-2.5 text-[13px] leading-5 text-secondary-text break-words select-text"
        >
          {{ overlayState.dialog.message }}
        </DialogDescription>
      </DialogHeader>

      <!-- 前端 prompt() 扩展：原版 NyaPromptHost 无输入框，这里补一个文本输入 -->
      <div v-if="overlayState.dialog?.input" class="mt-4">
        <Input
          ref="inputRef"
          v-model="inputValue"
          type="text"
          :placeholder="overlayState.dialog.input.placeholder || ''"
          @keydown.enter="submit"
        />
      </div>

      <DialogFooter class="mt-5 gap-1.5 sm:justify-end">
        <Button
          v-for="btn in overlayState.dialog?.buttons ?? []"
          :key="btn.id"
          :variant="btn.default ? 'default' : 'ghost'"
          size="sm"
          @click="onClick(btn)"
        >{{ btn.label }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup>
import { computed, ref, watch, nextTick } from 'vue'
import Icon from './Icon.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog'
import { overlayState, mapSeverity, completeDialog } from './state.js'

const inputRef = ref(null)
const inputValue = ref('')

const mapped = computed(() => mapSeverity(overlayState.dialog?.severity))

watch(() => overlayState.dialog, (dialog) => {
  inputValue.value = dialog?.input?.value ?? ''
  if (dialog?.input) {
    // Input 是 script-setup 组件：$el 即原生 input
    nextTick(() => (inputRef.value?.$el ?? inputRef.value)?.focus())
  }
})

function onClick(btn) {
  const dialog = overlayState.dialog
  completeDialog(dialog?.input ? { id: btn.id, value: inputValue.value } : btn.id)
}

function submit() {
  const dialog = overlayState.dialog
  const defaultBtn = dialog?.buttons.find((b) => b.default) ?? dialog?.buttons[0]
  if (defaultBtn) onClick(defaultBtn)
}
</script>
