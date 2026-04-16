<script setup lang="ts">
import { useChatStore } from '@seren/ui-shared'
import { watch } from 'vue'
import { mobileWebSocketFactory } from './bridge/WebSocketBridge'
import { useConnectionStore } from './stores/connection'

const chatStore = useChatStore()
const connectionStore = useConnectionStore()

// Reconnect whenever the user re-scans a QR code or wipes config from settings.
watch(
  () => connectionStore.config,
  (next) => {
    if (!next) {
      chatStore.disconnect()
      return
    }
    chatStore.initClient(next.wsUrl, {
      token: next.token || undefined,
      webSocketFactory: mobileWebSocketFactory,
    })
  },
  { immediate: true },
)
</script>

<template>
  <div class="app-container">
    <header class="app-header">
      <h1>Seren</h1>
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
  min-height: 100dvh;
  background: #f8fafc;
  /* Safe area insets for mobile */
  padding-top: env(safe-area-inset-top);
  padding-bottom: env(safe-area-inset-bottom);
}
.app-header {
  display: flex;
  align-items: center;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #e2e8f0;
  background: #fff;
}
.app-header h1 {
  font-size: 1.125rem;
  font-weight: 600;
  color: #1e293b;
}
.app-main {
  flex: 1;
  overflow: hidden;
}
</style>