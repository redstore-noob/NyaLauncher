<template>
  <section class="page">
    <header class="page-head">
      <h1 class="m-0 text-[28px] font-bold text-primary-text">AI 设置</h1>
      <p class="m-0 text-[13px] text-hint-text">AI 助手功能开关（后端待移植）</p>
    </header>

    <div class="cards">
      <!-- 对照原 LauncherSettingsPage.axaml 的 CardAi 卡片还原；
           原独立 AiSettingsPage.axaml 为空占位，AI 逻辑承载于 AiSettingsStore（未移植）。 -->
      <SettingsCard icon="✨" title="AI 设置" subtitle="启用后的 AI 对话助手（PORTING_NOTES：AiSettingsStore 未移植，仅保留开关）">
        <SettingRow title="启用 AI 功能"
                    hint="PORTING_NOTES：AI 后端（OpenAI .NET 对应的对话服务）尚未随 bindings 移植，此开关仅本地持久化，待 ai 命令接入后生效。">
          <NyaToggle v-model="aiEnabled" @change="saveAiEnabled" />
        </SettingRow>

        <!-- 绑定缺失占位：待后端提供 AI 命令后替换为真实绑定 -->
        <SettingRow title="模型 / API 密钥配置"
                    hint="PORTING_NOTES：绑定缺失占位——后端 AI 服务未移植，模型选择与密钥管理待接入。">
          <Button variant="secondary" size="sm" disabled>待后端支持</Button>
        </SettingRow>
      </SettingsCard>
    </div>
  </section>
</template>

<script setup>
/*
 * AiSettingsView —— AiSettingsPage 还原。
 * 原版 AiSettingsPage.axaml 为空占位（逻辑「后续接入 AiSettingsStore」），
 * AI 卡片实际位于原 LauncherSettingsPage；此处集中还原该卡片。
 * PORTING_NOTES：AI 后端命令未出现在 internal/bindings 12 个 API 中，
 * 「启用 AI 功能」开关暂存 localStorage（key: nyalauncher.ai.enabled），其余项为绑定缺失占位。
 */
import { ref } from 'vue';
import { SettingsCard, SettingRow, NyaToggle } from '../../components/settings/index.js';
import { Button } from '@/components/ui/button';

const AI_KEY = 'nyalauncher.ai.enabled';
const aiEnabled = ref(localStorage.getItem(AI_KEY) === '1');
function saveAiEnabled() {
  localStorage.setItem(AI_KEY, aiEnabled.value ? '1' : '0');
}
</script>

<style scoped>
@import './settings-shared.css';
</style>
