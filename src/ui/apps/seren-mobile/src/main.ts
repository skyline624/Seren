import { createSerenI18n } from '@seren/i18n'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import App from './App.vue'
import IndexPage from './pages/index.vue'
import OnboardingPage from './pages/onboarding.vue'
import { useConnectionStore } from './stores/connection'
import 'virtual:uno.css'

const app = createApp(App)
app.use(createPinia())
app.use(createSerenI18n())

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: IndexPage, name: 'home' },
    { path: '/onboarding', component: OnboardingPage, name: 'onboarding' },
  ],
})

router.beforeEach(async (to) => {
  const store = useConnectionStore()
  await store.hydrate()

  if (!store.isConfigured && to.name !== 'onboarding') {
    return { name: 'onboarding' }
  }
  if (store.isConfigured && to.name === 'onboarding') {
    return { name: 'home' }
  }
  return true
})

app.use(router)
app.mount('#app')