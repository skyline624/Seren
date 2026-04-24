<script setup lang="ts">
import { computed } from 'vue'
import { ChatPanel, AvatarStage, useCharacterStore } from '@seren/ui-shared'

const characterStore = useCharacterStore()

// Each character may override the avatar model path (Live2D
// `.model3.json` URL). When null, `AvatarStage` falls back to the
// bundled Hiyori model (`/avatars/live2d/hiyori/Hiyori.model3.json`).
const avatarModelUrl = computed<string | undefined>(
  () => characterStore.activeCharacter?.avatarModelPath ?? undefined,
)
</script>

<template>
  <div class="home-viewport">
    <div class="avatar-fullscreen">
      <AvatarStage :model-url="avatarModelUrl" />
    </div>

    <div class="chat-overlay">
      <ChatPanel />
    </div>

    <div class="wave-container">
      <svg class="wave wave--back" viewBox="0 0 1440 120" preserveAspectRatio="none">
        <path d="M0,60 C240,120 480,0 720,60 C960,120 1200,0 1440,60 L1440,120 L0,120 Z" fill="rgba(13, 148, 136, 0.08)" />
      </svg>
      <svg class="wave wave--mid" viewBox="0 0 1440 120" preserveAspectRatio="none">
        <path d="M0,80 C360,20 720,100 1080,40 C1260,10 1380,60 1440,80 L1440,120 L0,120 Z" fill="rgba(13, 148, 136, 0.12)" />
      </svg>
      <svg class="wave wave--front" viewBox="0 0 1440 120" preserveAspectRatio="none">
        <path d="M0,90 C180,50 360,110 540,70 C720,30 900,90 1080,60 C1260,30 1380,80 1440,90 L1440,120 L0,120 Z" fill="rgba(13, 148, 136, 0.18)" />
      </svg>
    </div>
  </div>
</template>

<style scoped>
.home-viewport {
  position: relative;
  width: 100%;
  height: 100%;
  overflow: hidden;
}

.avatar-fullscreen {
  position: absolute;
  inset: 0;
  z-index: 1;
}

.chat-overlay {
  position: absolute;
  right: 1rem;
  top: 50%;
  transform: translateY(-50%);
  z-index: 10;
  width: min(500px, 35vw);
  min-width: 320px;
  height: 85vh;
}

.wave-container {
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  height: 120px;
  z-index: 5;
  pointer-events: none;
  overflow: hidden;
}

.wave {
  position: absolute;
  bottom: 0;
  left: 0;
  width: 200%;
  height: 100%;
}

.wave--back {
  animation: waveScroll 18s linear infinite;
  opacity: 0.6;
}

.wave--mid {
  animation: waveScroll 12s linear infinite reverse;
  opacity: 0.8;
}

.wave--front {
  animation: waveScroll 8s linear infinite;
}

@keyframes waveScroll {
  0% { transform: translateX(0); }
  100% { transform: translateX(-50%); }
}

@media (max-width: 768px) {
  .chat-overlay {
    right: 0.5rem;
    left: 0.5rem;
    width: auto;
    min-width: unset;
    height: 55vh;
    top: auto;
    bottom: 0.5rem;
    transform: none;
  }

  .wave-container {
    display: none;
  }
}
</style>
