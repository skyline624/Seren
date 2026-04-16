import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import { createSerenI18n } from '@seren/i18n'
import 'virtual:uno.css'
import App from './App.vue'
import IndexPage from './pages/index.vue'

const app = createApp(App)
app.use(createPinia())
app.use(createSerenI18n())

const router = createRouter({
  history: createWebHistory(),
  routes: [{ path: '/', component: IndexPage }],
})
app.use(router)
app.mount('#app')