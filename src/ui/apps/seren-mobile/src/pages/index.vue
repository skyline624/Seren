<script setup lang="ts">
import { ref } from 'vue'
import { ChatPanel, AvatarStage } from '@seren/ui-shared'

const activeTab = ref<'avatar' | 'chat'>('chat')
</script>

<template>
  <div class="index-page">
    <div v-if="activeTab === 'avatar'" class="tab-content">
      <AvatarStage avatar-mode="vrm" model-url="/avatar.vrm" />
    </div>
    <div v-if="activeTab === 'chat'" class="tab-content">
      <ChatPanel />
    </div>
    <nav class="tab-bar">
      <button :class="['tab-button', { active: activeTab === 'avatar' }]" @click="activeTab = 'avatar'">
        Avatar
      </button>
      <button :class="['tab-button', { active: activeTab === 'chat' }]" @click="activeTab = 'chat'">
        Chat
      </button>
    </nav>
  </div>
</template>

<style scoped>
.index-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  height: 100dvh;
}
.tab-content {
  flex: 1;
  overflow: hidden;
}
.tab-bar {
  display: flex;
  border-top: 1px solid #e2e8f0;
  background: #fff;
  padding-bottom: env(safe-area-inset-bottom);
}
.tab-button {
  flex: 1;
  padding: 0.75rem;
  background: none;
  border: none;
  font-size: 0.875rem;
  color: #64748b;
  cursor: pointer;
  transition: color 0.15s;
}
.tab-button.active {
  color: #3b82f6;
  font-weight: 600;
}
</style>