<template>
  <!-- 底部左侧警示滑条（NyaAlertHost.axaml：左下滑入，强调条颜色随级别） -->
  <transition name="nya-alert">
    <div
      v-if="overlayState.alertVisible && overlayState.alert"
      class="fixed left-5 bottom-[46px] z-[940] flex max-w-[440px] items-stretch gap-0
             rounded-xl border border-input bg-popover py-[9px] shadow-lg"
    >
      <span class="mx-0 my-px w-1 shrink-0 rounded-full" :style="{ background: strip.color }" />
      <span class="ml-[11px] flex flex-none items-center" :style="{ color: strip.color }">
        <Icon :name="strip.icon" :size="18" />
      </span>
      <span class="mx-2.5 self-center text-xs leading-[17px] text-body-text break-words select-text">
        {{ overlayState.alert.message }}
      </span>
      <button
        class="mr-2 h-[26px] w-[26px] flex-none self-center inline-flex items-center justify-center
               rounded-md text-hint-text transition-colors hover:bg-muted hover:text-foreground"
        title="关闭"
        @click="hideAlertNow"
      >
        <Icon name="close" :size="12" />
      </button>
    </div>
  </transition>
</template>

<script setup>
import { computed } from 'vue'
import Icon from './Icon.vue'
import { overlayState, mapSeverity, hideAlertNow } from './state.js'

const strip = computed(() =>
  mapSeverity(overlayState.alert?.severity))
</script>

<style scoped>
/* 进入：淡入 + 左侧滑入 -48px（emphasized-decelerate，300ms）；
   退出：淡出 + 滑出（emphasized-accelerate，200ms）——参数与原版一致，保持不变 */
.nya-alert-enter-active {
  transition: opacity 120ms linear, transform 300ms var(--ease-emphasized-decelerate);
}
.nya-alert-leave-active {
  transition: opacity 60ms linear, transform 200ms var(--ease-emphasized-accelerate);
}
.nya-alert-enter-from { opacity: 0; transform: translateX(-48px); }
.nya-alert-leave-to { opacity: 0; transform: translateX(-48px); }
</style>
