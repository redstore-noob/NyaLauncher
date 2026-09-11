<template>
  <!-- 遮罩层宿主（ModalOverlayHost.axaml：点击空白处不关闭，由内容逻辑发起关闭） -->
  <transition name="nya-modal">
    <div v-if="show" class="fixed inset-0 z-[900] flex items-center justify-center bg-overlay">
      <div class="nya-modal-content flex items-center justify-center">
        <slot />
      </div>
    </div>
  </transition>
</template>

<script setup>
defineProps({
  show: { type: Boolean, default: false },
})
</script>

<style scoped>
/* 出入场：遮罩淡入淡出 + 内容上浮（OverlayEffects.PopIn/PopOut，原版令牌） */
.nya-modal-enter-active { transition: opacity 300ms var(--ease-emphasized-decelerate); }
.nya-modal-leave-active { transition: opacity 180ms var(--ease-emphasized-accelerate); }
.nya-modal-enter-from, .nya-modal-leave-to { opacity: 0; }
.nya-modal-enter-active .nya-modal-content { transition: transform 300ms var(--ease-emphasized-decelerate); }
.nya-modal-enter-from .nya-modal-content { transform: translateY(16px); }
</style>
