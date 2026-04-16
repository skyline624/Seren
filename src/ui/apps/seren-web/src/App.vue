<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { useChatStore } from '@seren/ui-shared'

const chatStore = useChatStore()

onMounted(() => {
  // Connect to Seren hub via WebSocket
  // Use relative URL so Vite proxy forwards to backend
  const wsUrl = `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}/ws`
  chatStore.initClient(wsUrl)
})

onUnmounted(() => {
  chatStore.disconnect()
})
</script>

<template>
  <div class="app-container">
    <header class="app-header">
      <h1 text-xl font-bold>Seren</h1>
      <span text-sm text-gray-500>AI Avatar Hub</span>
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

.app-main {
  flex: 1;
  overflow: hidden;
}
</style>