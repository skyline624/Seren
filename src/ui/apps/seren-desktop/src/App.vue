<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { useChatStore } from '@seren/ui-shared'

const chatStore = useChatStore()

onMounted(() => {
  // In Tauri, we connect to the local hub directly
  chatStore.initClient('ws://localhost:5000/ws')
})

onUnmounted(() => {
  chatStore.disconnect()
})
</script>

<template>
  <div class="app-container" data-tauri-drag-region>
    <header class="app-header" data-tauri-drag-region>
      <h1 data-tauri-drag-region>Seren</h1>
      <span>Desktop</span>
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
  padding: 0.5rem 1rem;
  border-bottom: 1px solid #e2e8f0;
  background: #fff;
  -webkit-user-select: none;
  user-select: none;
}
.app-main {
  flex: 1;
  overflow: hidden;
}
</style>