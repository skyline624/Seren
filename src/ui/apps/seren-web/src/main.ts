import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import { createSerenI18n } from '@seren/i18n'
import 'virtual:uno.css'
// Seren design tokens — must load before component styles so
// `:root { --airi-* }` is set up before anything paints.
import './styles/tokens.css'
import '@seren/ui-shared/style.css'
import '@seren/ui-live2d/style.css'
import App from './App.vue'
import IndexPage from './pages/index.vue'
import SettingsPage from './pages/settings.vue'

const app = createApp(App)

const pinia = createPinia()
app.use(pinia)

const i18n = createSerenI18n()
app.use(i18n)

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: IndexPage },
    { path: '/settings', component: SettingsPage },
  ],
})
app.use(router)

app.mount('#app')
