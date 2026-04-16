<script setup lang="ts">
import { computed } from 'vue'
import { ChatPanel, AvatarStage, CharacterSelector, useCharacterStore, useSettingsStore } from '@seren/ui-shared'

const characterStore = useCharacterStore()
const settingsStore = useSettingsStore()

const avatarModelUrl = computed(() =>
  characterStore.activeCharacter?.vrmAssetPath ?? undefined,
)

const avatarMode = computed(() => settingsStore.avatarMode)
</script>

<template>
  <div class="index-page">
    <div class="avatar-section">
      <AvatarStage :avatar-mode="avatarMode" :model-url="avatarModelUrl" />
      <CharacterSelector />
    </div>
    <div class="chat-section">
      <ChatPanel />
    </div>
  </div>
</template>

<style scoped>
.index-page {
  height: 100%;
  display: flex;
  gap: 0.5rem;
  padding: 1rem;
}

.avatar-section {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  min-width: 0;
}

.chat-section {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

/* Mobile: stack vertically */
@media (max-width: 768px) {
  .index-page {
    flex-direction: column;
  }

  .avatar-section {
    max-height: 40vh;
  }

  .chat-section {
    flex: 1;
  }
}
</style>
