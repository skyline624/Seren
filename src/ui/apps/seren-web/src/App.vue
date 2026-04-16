<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { useChatStore, useCharacterStore } from '@seren/ui-shared'

const chatStore = useChatStore()
const characterStore = useCharacterStore()

onMounted(() => {
  // Connect to Seren hub via WebSocket
  const wsUrl = `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}/ws`
  chatStore.initClient(wsUrl)

  // Initialize character store with REST base URL
  characterStore.setBaseUrl(wsUrl)
  characterStore.fetchAll()
})

onUnmounted(() => {
  chatStore.disconnect()
})
</script>

<template>
  <div class="app-container">
    <header class="app-header">
      <div class="app-header__left">
        <RouterLink to="/" class="app-header__brand">
          <h1 text-xl font-bold>Seren</h1>
        </RouterLink>
        <span text-sm text-gray-500>AI Avatar Hub</span>
      </div>
      <nav class="app-header__nav">
        <RouterLink to="/settings" class="app-header__link">
          Settings
        </RouterLink>
      </nav>
    </header>
    <main class="app-main">
      <RouterView />
    </main>
  </div>
</template>

<style scoped>
.app-container {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  max-height: 100vh;
  background: #f8fafc;
}

.app-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1.5rem;
  border-bottom: 1px solid #e2e8f0;
  background: #fff;
}

.app-header__left {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.app-header__brand {
  text-decoration: none;
  color: inherit;
}

.app-header__nav {
  display: flex;
  gap: 0.75rem;
}

.app-header__link {
  font-size: 0.875rem;
  color: #3b82f6;
  text-decoration: none;
}

.app-header__link:hover {
  text-decoration: underline;
}

.app-main {
  flex: 1;
  overflow: hidden;
}
</style>
