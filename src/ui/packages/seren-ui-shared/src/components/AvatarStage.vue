<script setup lang="ts">
import { ref, shallowRef, markRaw, watch, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useChatStore } from '../stores/chat'
import { useAvatarSettingsStore } from '../stores/settings/avatar'

const props = defineProps<{
  avatarMode?: 'vrm' | 'live2d'
  modelUrl?: string
  /**
   * Mapping from `<action:NAME>` marker to a `.vrma` clip URL. Actions
   * not present here fall back to procedural humanoid-bone animations
   * in `useVRMGestures` (nod / bow / shake work well that way).
   */
  actionClipMap?: Record<string, string>
  /** Mapping from `<emotion:NAME>` marker to a `.vrma` clip URL. */
  emotionClipMap?: Record<string, string>
}>()

// Default VRMA clip map. Actions without an entry rely on the
// procedural fallback inside VRMViewer (`useVRMGestures`).
const DEFAULT_ACTION_CLIPS: Readonly<Record<string, string>> = Object.freeze({
  wave: '/animations/wave.vrma',
  think: '/animations/think.vrma',
})

const mergedActionClipMap = computed<Record<string, string>>(() => ({
  ...DEFAULT_ACTION_CLIPS,
  ...(props.actionClipMap ?? {}),
}))

const mergedEmotionClipMap = computed<Record<string, string>>(() => ({
  ...(props.emotionClipMap ?? {}),
}))

const chatStore = useChatStore()
const avatarSettings = useAvatarSettingsStore()
const {
  outlineEnabled,
  modelScale,
  positionY,
  rotationY,
  cameraDistance,
  cameraHeight,
  cameraFov,
  ambientIntensity,
  directionalIntensity,
  eyeTrackingMode,
  outlineThickness,
  outlineColor,
  outlineAlpha,
} = storeToRefs(avatarSettings)

const currentEmotion = ref<string>('neutral')
const renderError = ref<string | null>(null)

// `<input type="color">` yields `#RRGGBB`; VRMOutlinePass wants 0..1 RGB
// tuples. Lightweight converter — no hex shorthand handling needed since
// the native picker never produces it.
const outlineColorRgb = computed<[number, number, number]>(() => {
  const hex = outlineColor.value.replace(/^#/, '')
  if (hex.length !== 6) return [0, 0, 0]
  const r = Number.parseInt(hex.slice(0, 2), 16) / 255
  const g = Number.parseInt(hex.slice(2, 4), 16) / 255
  const b = Number.parseInt(hex.slice(4, 6), 16) / 255
  return [r, g, b]
})

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
      :action-clip-map="mergedActionClipMap"
      :emotion-clip-map="mergedEmotionClipMap"
      :outline="outlineEnabled"
      :outline-thickness="outlineThickness"
      :outline-color="outlineColorRgb"
      :outline-alpha="outlineAlpha"
      :model-scale="modelScale"
      :position-y="positionY"
      :rotation-y="rotationY"
      :camera-distance="cameraDistance"
      :camera-height="cameraHeight"
      :camera-fov="cameraFov"
      :ambient-intensity="ambientIntensity"
      :directional-intensity="directionalIntensity"
      :eye-tracking-mode="eyeTrackingMode"
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
