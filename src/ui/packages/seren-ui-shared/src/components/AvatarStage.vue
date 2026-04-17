<script setup lang="ts">
import { ref, shallowRef, markRaw, watch, computed } from 'vue'
import { useChatStore } from '../stores/chat'

const props = defineProps<{
  avatarMode?: 'vrm' | 'live2d'
  modelUrl?: string
}>()

const chatStore = useChatStore()
const currentEmotion = ref<string>('neutral')
const renderError = ref<string | null>(null)

// Watch the live `currentEmotion` ref (populated by the `avatar:emotion`
// handler mid-stream, before the assistant message exists in the history).
// Watching the last message's `emotion` alone would miss it because the
// message is only pushed at chat:end.
watch(() => chatStore.currentEmotion?.nonce, () => {
  const e = chatStore.currentEmotion?.emotion
  if (e) currentEmotion.value = e
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

// VRM viewer component (lazy loaded) — shallowRef + markRaw to prevent Vue
// from making the component definition reactive (which breaks TresJS context)
const VRMViewer = shallowRef<any>(null)
// Live2D viewer component (lazy loaded)
const Live2DViewer = shallowRef<any>(null)

async function loadVRMViewer(): Promise<void> {
  try {
    const mod = await import('@seren/ui-three')
    VRMViewer.value = markRaw(mod.VRMViewer)
  }
  catch {
    renderError.value = 'VRM renderer not available'
  }
}

async function loadLive2DViewer(): Promise<void> {
  try {
    const mod = await import('@seren/ui-live2d')
    Live2DViewer.value = markRaw(mod.Live2DViewer)
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
      :action="chatStore.currentAction"
      :thinking="chatStore.isThinking"
      :lipsync-frames="lipsyncFrames"
    />
    <component
      :is="Live2DViewer"
      v-if="mode === 'live2d' && Live2DViewer && modelUrl"
      :model-url="modelUrl"
      :emotion="currentEmotion"
      :action="chatStore.currentAction"
      :thinking="chatStore.isThinking"
      :lipsync-frames="lipsyncFrames"
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
  background: radial-gradient(ellipse at center bottom, #1a2a2e 0%, #121212 70%);
  border-radius: 0;
  overflow: hidden;
  position: relative;
}
.avatar-stage__placeholder {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #475569;
  font-size: 1rem;
  letter-spacing: 0.05em;
}
.avatar-stage__error {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fca5a5;
  font-size: 0.875rem;
  background: rgba(239, 68, 68, 0.05);
}
</style>
