<script setup lang="ts">
import { ref, shallowRef, markRaw, watch, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useChatStore } from '../stores/chat'
import { useAvatarSettingsStore } from '../stores/settings/avatar'
import { useAnimationSettingsStore } from '../stores/settings/animation'
import { useIdleAnimationScheduler } from '../composables/useIdleAnimationScheduler'
import type { IdleAnimation } from '../composables/idleAnimationCatalog'
import { avatarDebugLog } from '../composables/avatarDebugLog'
import { useAvatarLayerGains } from '../composables/useAvatarLayerGains'

const props = defineProps<{
  avatarMode?: 'vrm' | 'live2d'
  modelUrl?: string
  /**
   * Mapping from `<action:NAME>` marker to a `.vrma` clip URL. Actions
   * without an entry here are silently ignored by the renderer — there
   * is no procedural fallback (see `public/animations/README.md` for
   * the .vrma-authoring workflow).
   */
  actionClipMap?: Record<string, string>
  /** Mapping from `<emotion:NAME>` marker to a `.vrma` clip URL. */
  emotionClipMap?: Record<string, string>
}>()

// Default VRMA clip map for LLM-driven `<action:NAME>` markers. Add an
// entry here when you drop a new .vrma into `public/animations/`.
// See `src/ui/apps/seren-web/public/animations/README.md` for the
// sourcing + conversion workflow.
const DEFAULT_ACTION_CLIPS: Readonly<Record<string, string>> = Object.freeze({
  wave: '/animations/wave.vrma',
  think: '/animations/think.vrma',
})

// Default VRMA clip map for AUTO-FIRED idle animations (the scheduler
// picks one at random every few seconds when the avatar is idle).
// Kept separate from `DEFAULT_ACTION_CLIPS` because:
//  - action-clips fire ON DEMAND (LLM / user triggers a `<action:NAME>`);
//  - idle-clips fire SPONTANEOUSLY and must stay short + non-intrusive.
// Add any .vrma you'd like the avatar to loop through during pauses.
const DEFAULT_IDLE_CLIPS: Readonly<Record<string, string>> = Object.freeze({
  pixiv_demo: '/animations/pixiv_demo.vrma',
})

// Single source of truth for every .vrma the renderer can play:
// action-triggered clips + idle-triggered clips, merged so the
// VRMViewer sees a flat `Record<actionId, url>` regardless of which
// pipeline fires it.
const mergedActionClipMap = computed<Record<string, string>>(() => ({
  ...DEFAULT_ACTION_CLIPS,
  ...DEFAULT_IDLE_CLIPS,
  ...(props.actionClipMap ?? {}),
}))

// Data-driven scheduler catalog: one entry per registered idle clip.
// Weights stay flat (neutral: 1.0) until we surface per-mood tuning
// as a user setting — no point biasing selection when the pool is
// tiny. The array re-computes when DEFAULT_IDLE_CLIPS gains/loses
// entries, keeping it aligned with the clip map (DRY).
const idleCatalog = computed<readonly IdleAnimation[]>(() =>
  Object.keys(DEFAULT_IDLE_CLIPS).map(id => ({
    id,
    durationMs: 2000,
    moodWeights: { neutral: 1.0 },
  })),
)

const mergedEmotionClipMap = computed<Record<string, string>>(() => ({
  ...(props.emotionClipMap ?? {}),
}))

const chatStore = useChatStore()
const avatarSettings = useAvatarSettingsStore()
const animationSettings = useAnimationSettingsStore()
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
// [0, 1] — LLM markers stamp 1.0, text-classifier predictions stamp
// their confidence. Forwarded to VRMViewer so `useVRMEmote` can scale
// the blendshape peak accordingly.
const currentEmotionIntensity = ref<number>(1)
const renderError = ref<string | null>(null)

// Per-phase layer multipliers (Phase 5). Fully derived from the chat
// store via `useAvatarStateStore` — zero mutation surface here.
const layerGains = useAvatarLayerGains()

// ── Idle animation scheduler (Tier 1) ─────────────────────────────
// Fires micro-animations during pauses. Reuses the existing
// `chatStore.currentAction` channel so renderers pick it up through
// their existing watchers — no new plumbing.
const idleIsActive = computed(() =>
  !chatStore.isStreaming
  && !chatStore.isThinking
  && chatStore.connectionStatus === 'ready',
)
const idleMood = computed<string | null>(() =>
  chatStore.currentEmotion?.emotion ?? null,
)
const idleIntervalSeconds = computed<readonly [number, number]>(
  () => animationSettings.idleIntervalSeconds,
)

useIdleAnimationScheduler({
  isActive: idleIsActive,
  mood: idleMood,
  intervalSeconds: idleIntervalSeconds,
  enabled: computed(() => animationSettings.idleEnabled),
  catalog: idleCatalog.value,
  onTrigger: (animation) => {
    avatarDebugLog('idle', 'trigger', { id: animation.id, mood: idleMood.value })
    // Fire through the standard action channel so both VRM + Live2D
    // renderers pick it up identically. Nonce uses Date.now() (same
    // convention as hub-driven actions in chat.ts).
    chatStore.currentAction = { action: animation.id, nonce: Date.now() }
  },
})

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
  const payload = chatStore.currentEmotion
  if (!payload?.emotion) return
  currentEmotion.value = payload.emotion
  // Explicit markers default to 1, classifier emits its score. Clamp
  // defensively — bad data upstream should still produce a sane face.
  const raw = payload.intensity ?? 1
  currentEmotionIntensity.value = Math.max(0, Math.min(1, raw))
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
      :blink-enabled="animationSettings.blinkEnabled"
      :saccade-enabled="animationSettings.saccadeEnabled"
      :body-sway-enabled="animationSettings.bodySwayEnabled"
      :emotion-intensity="currentEmotionIntensity"
      :layer-gains="layerGains"
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
