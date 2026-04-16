<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import { useChatStore } from '../stores/chat'

const props = defineProps<{
  avatarMode?: 'vrm' | 'live2d'
  modelUrl?: string
}>()

const chatStore = useChatStore()
const currentEmotion = ref<string>('neutral')
const renderError = ref<string | null>(null)

// The current emotion from the chat store's last message
watch(() => {
  const last = chatStore.messages.at(-1)
  return last?.emotion
}, (emotion) => {
  if (emotion) currentEmotion.value = emotion
})

// Lipsync frames from the store, converted to the format expected by VRMViewer
const lipsyncFrames = computed(() =>
  chatStore.lipsyncFrames.map(f => ({
    viseme: f.viseme,
    startTime: f.startTime,
    duration: f.duration,
    weight: f.weight,
  })),
)

// VRM viewer component (lazy loaded)
const VRMViewer = ref<any>(null)
// Live2D viewer component (lazy loaded)
const Live2DViewer = ref<any>(null)

async function loadVRMViewer(): Promise<void> {
  try {
    const mod = await import('@seren/ui-three')
    VRMViewer.value = mod.VRMViewer
  }
  catch {
    renderError.value = 'VRM renderer not available'
  }
}

async function loadLive2DViewer(): Promise<void> {
  try {
    const mod = await import('@seren/ui-live2d')
    Live2DViewer.value = mod.Live2DViewer
  }
  catch {
    renderError.value = 'Live2D renderer not available'
  }
}

const mode = computed(() => props.avatarMode ?? 'vrm')

// Load the appropriate renderer
if (mode.value === 'vrm') loadVRMViewer()
else loadLive2DViewer()

watch(() => props.avatarMode, (newMode) => {
  renderError.value = null
  if (newMode === 'vrm' || (!newMode && !Live2DViewer.value)) loadVRMViewer()
  else loadLive2DViewer()
})
</script>

<template>
  <div class="avatar-stage">
    <div v-if="renderError" class="avatar-stage__error">
      {{ renderError }}
    </div>
    <component
      :is="VRMViewer"
      v-if="mode === 'vrm' && VRMViewer && modelUrl"
      :model-url="modelUrl"
      :emotion="currentEmotion"
      :lipsync-frames="lipsyncFrames"
    />
    <component
      :is="Live2DViewer"
      v-if="mode === 'live2d' && Live2DViewer && modelUrl"
      :model-url="modelUrl"
      :emotion="currentEmotion"
    />
    <div v-if="!modelUrl" class="avatar-stage__placeholder">
      No avatar loaded
    </div>
  </div>
</template>

<style scoped>
.avatar-stage {
  width: 100%;
  height: 100%;
  background: linear-gradient(135deg, #f8fafc 0%, #e2e8f0 100%);
  border-radius: 8px;
  overflow: hidden;
  position: relative;
}
.avatar-stage__placeholder {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #94a3b8;
  font-size: 0.875rem;
}
.avatar-stage__error {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #ef4444;
  font-size: 0.875rem;
}
</style>
