<script setup lang="ts">
import { ChatPanel, AvatarStage } from '@seren/ui-shared'
</script>

<template>
  <div class="index-page" h-full flex>
    <div class="avatar-section" w="1/2" p-4>
      <AvatarStage avatar-mode="vrm" model-url="/avatar.vrm" />
    </div>
    <div class="chat-section" w="1/2" p-4>
      <ChatPanel />
    </div>
  </div>
</template>

<style scoped>
.index-page { height: 100%; display: flex; }
.avatar-section { display: flex; flex-direction: column; }
.chat-section { display: flex; flex-direction: column; }
</style>