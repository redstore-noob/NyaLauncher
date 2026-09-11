<template>
  <section class="page">
    <header class="page-head">
      <h1 class="m-0 text-[28px] font-bold text-primary-text">关于</h1>
      <p class="m-0 text-[13px] text-hint-text">NyaLauncher 开发团队、第三方组件与项目信息</p>
    </header>

    <div class="cards">
      <!-- 贡献者名单：连点卡片 7 次（2 秒内）触发猫娘彩蛋 -->
      <section class="about-card" @click="onContributorsClick">
        <h3 class="m-0 text-[18px] font-semibold text-primary-text">贡献者名单</h3>
        <div class="flex flex-wrap gap-2">
          <button
            v-for="c in contributors"
            :key="c.url"
            class="inline-flex cursor-pointer items-center gap-1.5 rounded-full border-none bg-badge px-3 py-1.5 text-[12px] font-semibold text-primary transition-[filter,transform] duration-150 hover:brightness-95 active:scale-95"
            @click.stop="openLink(c.url)"
          >
            <span class="text-[13px]">🤝</span>{{ c.name }}
          </button>
        </div>
        <p class="m-0 text-[11px] text-hint-text">感谢每一位通过 PR / Issue 为项目出力的伙伴，点个名字就能去 TA 的主页看看喵～</p>
        <p v-if="easterEggHint" class="m-0 text-[11px] text-primary">{{ easterEggHint }}</p>
      </section>

      <!-- 引用的库与资源（文案照抄原 AboutPage.axaml） -->
      <section class="about-card">
        <h3 class="m-0 text-[18px] font-semibold text-primary-text">引用的库与资源</h3>
        <div class="grid grid-cols-[160px_1fr] gap-x-2 gap-y-2.5">
          <template v-for="lib in libs" :key="lib.name">
            <span class="self-center text-[13px] text-muted-foreground">{{ lib.name }}</span>
            <span class="whitespace-pre-line text-[13px] leading-relaxed text-body-text">{{ lib.desc }}</span>
          </template>
        </div>
      </section>

      <!-- 项目信息 -->
      <section class="about-card">
        <h3 class="m-0 text-[18px] font-semibold text-primary-text">项目信息</h3>
        <p class="m-0 text-[14px] leading-relaxed text-body-text">{{ versionText || 'NyaLauncher' }}</p>
        <p class="m-0 text-[14px] leading-relaxed text-body-text">最后补丁日期: {{ patchDate }}</p>
        <p class="m-0 text-[14px] leading-relaxed text-body-text">该软件基于 Apache License 2.0 分发。</p>
        <p class="m-0 text-[14px] leading-relaxed text-body-text">该软件为NyaLauncher team编写，与Mojang Studios与Microsoft没有直接联系。</p>
        <p class="m-0 text-[11px] text-hint-text">https://github.com/redstore-noob/NyaLauncher</p>
        <Button variant="link" class="self-start px-0" @click="openLink('https://github.com/redstore-noob/NyaLauncher')">
          项目链接喵~Ciallo～(∠・ω&lt; )⌒★
        </Button>
        <div class="flex items-center gap-2">
          <Button variant="secondary" size="sm" :disabled="clearingLogs" @click="clearLogs">清理日志</Button>
          <span v-if="clearLogsHint" class="text-[11px] text-hint-text">{{ clearLogsHint }}</span>
        </div>
      </section>

      <!-- QQ 群 -->
      <section class="about-card flex-row items-center justify-between gap-4">
        <div class="flex flex-col gap-1">
          <h3 class="m-0 text-[18px] font-semibold text-primary-text">加入我们</h3>
          <p class="m-0 text-[14px] leading-relaxed text-body-text">QQ 群：1108330006 · 反馈建议 · 版本抢先体验 · 日常摸鱼</p>
        </div>
        <Button @click="copyGroup">复制群号</Button>
      </section>
    </div>

    <!-- 猫娘彩蛋覆盖层：连点贡献者卡片 7 次触发；点击任意处关闭 -->
    <Teleport to="body">
      <div v-if="nekoVisible" class="fixed inset-0 z-[1000] flex items-center justify-center bg-overlay" @click="nekoVisible = false">
        <div class="neko-card flex max-w-[420px] flex-col items-center gap-2.5 rounded-2xl border border-default-border bg-card p-4">
          <div class="text-[72px] leading-tight">🐱🎀</div>
          <p class="m-0 text-[15px] font-semibold text-primary-text">奈娅(Nya)美图🥰</p>
          <p class="m-0 text-[11px] text-hint-text">PORTING_NOTES：原版彩蛋图片（neko-girl.png）为 Avalonia 资源，前端资源未随移植，先以占位呈现。</p>
          <p class="m-0 text-[11px] text-hint-text">点击任意处关闭</p>
        </div>
      </div>
    </Teleport>
  </section>
</template>

<script setup>
/*
 * AboutSettingsView —— 还原 AboutPage：贡献者名单 / 引用的库与资源 / 项目信息 / QQ 群 / 猫娘彩蛋。
 * 真实绑定：SystemAPI.GetFormattedVersion（版本串）、GetAppVersion（补丁日期行）、ClearLogs 备用。
 * 开源许可与第三方组件文案照抄原 AboutPage.axaml。
 */
import { onMounted, ref } from 'vue';
import { ClearLogs, GetAppVersion, GetFormattedVersion } from '../../../wailsjs/go/bindings/SystemAPI.js';
import { Button } from '@/components/ui/button';

const QQGroupNumber = '1108330006';
const contributors = [
  { name: 'Mystic Stars', url: 'https://github.com/Mystic-Stars' },
  { name: 'TouristH', url: 'https://github.com/TouristH' },
];
const libs = [
  { name: 'Avalonia UI', desc: '12.1.1 · 跨平台 UI 框架（MIT License） https://github.com/AvaloniaUI/Avalonia' },
  { name: 'Material.Avalonia', desc: '3.19.0 · Material Design 风格与控件库（MIT License） https://github.com/AvaloniaCommunity/Material.Avalonia' },
  { name: 'Material.Icons.Avalonia', desc: '3.0.2 · Material Design 图标库（MIT License） https://github.com/SKProCH/Material.Icons' },
  { name: 'CommunityToolkit.Mvvm', desc: '8.4.2 · 现代化 MVVM 工具包（MIT License） https://github.com/CommunityToolkit/dotnet' },
  { name: 'NAudio', desc: '2.2.1 · 音乐播放器音频后端（MS-PL License） https://github.com/naudio/NAudio' },
  { name: 'OpenAI .NET', desc: '2.13.0 · AI 对话接口调用（OpenAI 官方 SDK · Apache-2.0） https://github.com/openai/openai-dotnet' },
  { name: 'BMCLAPI', desc: 'Minecraft 下载镜像服务（bangbang93） https://bmclapi2.bangbang93.com' },
];

const versionText = ref('');
const patchDate = ref('');
const clearingLogs = ref(false);
const clearLogsHint = ref('');

async function clearLogs() {
  clearingLogs.value = true;
  try {
    const removed = await ClearLogs();
    clearLogsHint.value = removed > 0 ? `已清理 ${removed} 个日志文件。` : '没有可清理的日志。';
  } catch (err) {
    clearLogsHint.value = `清理失败：${err?.message || err}`;
  } finally {
    clearingLogs.value = false;
  }
}

/* 猫娘彩蛋：连点 7 次（2 秒内） */
const NEKO_TRIGGER_CLICKS = 7;
let devClickCount = 0;
let devResetTimer = null;
const easterEggHint = ref('');
const nekoVisible = ref(false);

function onContributorsClick() {
  devClickCount++;
  clearTimeout(devResetTimer);
  devResetTimer = setTimeout(() => {
    devClickCount = 0;
    easterEggHint.value = '';
  }, 2000);
  if (devClickCount < NEKO_TRIGGER_CLICKS) {
    easterEggHint.value = `连点贡献者卡片有惊喜（还需 ${NEKO_TRIGGER_CLICKS - devClickCount} 次）`;
    return;
  }
  devClickCount = 0;
  clearTimeout(devResetTimer);
  easterEggHint.value = '';
  nekoVisible.value = true;
}

function openLink(url) {
  window.open(url, '_blank', 'noopener');
}

async function copyGroup() {
  try {
    await navigator.clipboard.writeText(QQGroupNumber);
    easterEggHint.value = `群号已复制：${QQGroupNumber}，欢迎来玩喵~`;
    setTimeout(() => { easterEggHint.value = ''; }, 2000);
  } catch {
    easterEggHint.value = `剪贴板不可用，请手动记录群号：${QQGroupNumber}`;
  }
}

onMounted(async () => {
  try {
    versionText.value = await GetFormattedVersion();
    patchDate.value = await GetAppVersion();
  } catch { /* 绑定不可用时保持静态文案 */ }
});
</script>

<style scoped>
@import './settings-shared.css';

/* 换装说明：卡片/徽章/文字均改为模板内 Tailwind 语义工具类（bg-card / card-border /
   rounded-2xl / bg-badge / bg-overlay 等），布局结构沿用 .cards / .about-card。 */
.about-card {
  display: flex;
  flex-direction: column;
  gap: var(--space-12);
  border-radius: var(--radius-xl);
  padding: 20px 24px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  box-shadow: 0 1px 2px 0 rgb(0 0 0 / 0.05);
  transition: box-shadow 0.2s var(--ease-emphasized), transform 0.2s var(--ease-emphasized);
}
.about-card:hover { box-shadow: 0 4px 12px 0 rgb(0 0 0 / 0.08); }

/* 猫娘彩蛋卡片入场（原 page-in：淡入 + 上浮 32px） */
.neko-card {
  animation: page-in var(--dur-medium) var(--ease-emphasized-decelerate);
}
@keyframes page-in {
  from { opacity: 0; transform: translateY(32px); }
  to { opacity: 1; transform: translateY(0); }
}
</style>
