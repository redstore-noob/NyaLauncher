import { createRouter, createWebHashHistory } from 'vue-router';

// 路由表：页面组件为占位文件，由后续页面移植 agent 填充实现。
const routes = [
  {
    path: '/',
    name: 'home',
    component: () => import('../views/HomeView.vue'),
    meta: { title: '工作区', nav: true },
  },
  {
    path: '/versions',
    name: 'versions',
    component: () => import('../views/VersionsView.vue'),
    meta: { title: '版本管理', nav: true },
  },
  {
    path: '/download',
    name: 'download',
    component: () => import('../views/DownloadView.vue'),
    meta: { title: '资源下载', nav: true },
  },
  {
    path: '/music',
    name: 'music',
    component: () => import('../views/MusicView.vue'),
    meta: { title: '音乐播放器', nav: true },
  },
  {
    path: '/modpack',
    name: 'modpack',
    component: () => import('../views/ModpackView.vue'),
    meta: { title: '整合包制作', nav: true },
  },
  {
    path: '/settings',
    component: () => import('../views/settings/Hub.vue'),
    meta: { title: '设置', nav: true },
    children: [
      { path: '', redirect: { name: 'settings-launcher' } },
      { path: 'launcher', name: 'settings-launcher', component: () => import('../views/settings/LauncherSettingsView.vue'), meta: { title: '启动器设置' } },
      { path: 'personalization', name: 'settings-personalization', component: () => import('../views/settings/PersonalizationSettingsView.vue'), meta: { title: '个性化' } },
      { path: 'ai', name: 'settings-ai', component: () => import('../views/settings/AiSettingsView.vue'), meta: { title: 'AI 设置' } },
      { path: 'about', name: 'settings-about', component: () => import('../views/settings/AboutView.vue'), meta: { title: '关于' } },
      { path: 'account', name: 'settings-account', component: () => import('../views/settings/AccountView.vue'), meta: { title: '账号管理' } },
    ],
  },
];

export const router = createRouter({
  history: createWebHashHistory(),
  routes,
});
