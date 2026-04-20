<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import {
  CharacterSelector,
  SettingsPanel,
  useAppearance,
  useAppearanceSettingsStore,
  useCharacterStore,
  useChatStore,
} from '@seren/ui-shared'

// Applies theme + primary-hue from the appearance store to the document.
// Must run before the first paint so stored prefs are visible immediately.
useAppearance()

// Propagate the stored locale into vue-i18n. Kept here (not in ui-shared)
// so the shared package stays free of a hard dependency on vue-i18n.
const { locale: appearanceLocale } = storeToRefs(useAppearanceSettingsStore())
const i18n = useI18n()
watch(appearanceLocale, (l) => { (i18n.locale as { value: string }).value = l }, { immediate: true })

const chatStore = useChatStore()
const characterStore = useCharacterStore()
const showSettings = ref(false)
const showCharacters = ref(false)

onMounted(() => {
  const wsUrl = `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}/ws`
  chatStore.initClient(wsUrl)
  characterStore.setBaseUrl(wsUrl)
  characterStore.fetchAll()
})

onUnmounted(() => {
  chatStore.disconnect()
})
</script>

<template>
  <div class="app-shell">
    <header class="app-topbar">
      <RouterLink to="/" class="app-topbar__brand">
        <svg viewBox="0 0 32 32" width="28" height="28">
          <circle cx="16" cy="16" r="14" fill="#0d9488" />
          <circle cx="12" cy="13" r="2.5" fill="#fff" />
          <circle cx="20" cy="13" r="2.5" fill="#fff" />
          <path d="M10 20 Q16 25 22 20" stroke="#fff" stroke-width="2" fill="none" stroke-linecap="round" />
        </svg>
        <span class="app-topbar__name">Seren</span>
      </RouterLink>
      <div class="app-topbar__actions">
        <button class="app-topbar__icon-btn" title="Characters" @click="showCharacters = !showCharacters">
          <svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor">
            <path d="M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z" />
          </svg>
        </button>
        <button class="app-topbar__icon-btn" title="Settings" @click="showSettings = !showSettings">
          <svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor">
            <path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.49.49 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.484.484 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96a.49.49 0 0 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.07.62-.07.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z" />
          </svg>
        </button>
      </div>
    </header>

    <main class="app-viewport">
      <RouterView />
    </main>

    <!-- Settings drawer -->
    <Teleport to="body">
      <div v-if="showSettings" class="drawer-overlay" @click.self="showSettings = false">
        <div class="drawer-panel">
          <div class="drawer-panel__header">
            <h2>Settings</h2>
            <button class="drawer-panel__close" @click="showSettings = false">&times;</button>
          </div>
          <SettingsPanel @open-character-editor="showCharacters = true; showSettings = false" />
        </div>
      </div>
    </Teleport>

    <!-- Characters drawer -->
    <Teleport to="body">
      <div v-if="showCharacters" class="drawer-overlay" @click.self="showCharacters = false">
        <div class="drawer-panel">
          <div class="drawer-panel__header">
            <h2>Characters</h2>
            <button class="drawer-panel__close" @click="showCharacters = false">&times;</button>
          </div>
          <CharacterSelector />
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.app-shell {
  position: relative;
  width: 100vw;
  height: 100vh;
  overflow: hidden;
  background: #121212;
  color: #e2e8f0;
}

.app-topbar {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  z-index: 50;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1.5rem;
}

.app-topbar__brand {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  text-decoration: none;
  color: #e2e8f0;
}

.app-topbar__name {
  font-size: 1.25rem;
  font-weight: 700;
  letter-spacing: 0.02em;
}

.app-topbar__actions {
  display: flex;
  gap: 0.5rem;
}

.app-topbar__icon-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  border: 1px solid rgba(100, 180, 200, 0.2);
  background: rgba(10, 30, 40, 0.5);
  backdrop-filter: blur(12px);
  color: #e2e8f0;
  cursor: pointer;
  transition: background 0.2s, border-color 0.2s;
}

.app-topbar__icon-btn:hover {
  background: rgba(13, 148, 136, 0.3);
  border-color: rgba(13, 148, 136, 0.5);
}

.app-viewport {
  width: 100%;
  height: 100%;
}

.drawer-overlay {
  position: fixed;
  inset: 0;
  z-index: 100;
  background: rgba(0, 0, 0, 0.5);
  backdrop-filter: blur(4px);
  display: flex;
  justify-content: flex-end;
}

.drawer-panel {
  width: 420px;
  max-width: 90vw;
  height: 100vh;
  background: rgba(10, 30, 40, 0.92);
  backdrop-filter: blur(20px);
  border-left: 1px solid rgba(100, 180, 200, 0.2);
  padding: 1.5rem;
  overflow-y: auto;
  animation: slideIn 0.25s ease-out;
}

.drawer-panel__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.5rem;
}

.drawer-panel__header h2 {
  font-size: 1.125rem;
  font-weight: 600;
  color: #e2e8f0;
  margin: 0;
}

.drawer-panel__close {
  width: 32px;
  height: 32px;
  border: none;
  background: rgba(100, 180, 200, 0.15);
  border-radius: 50%;
  color: #e2e8f0;
  font-size: 1.25rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
}

.drawer-panel__close:hover {
  background: rgba(239, 68, 68, 0.3);
}

@keyframes slideIn {
  from { transform: translateX(100%); }
  to { transform: translateX(0); }
}
</style>
