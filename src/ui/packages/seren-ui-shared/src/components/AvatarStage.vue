<script setup lang="ts">
import { ref, shallowRef, markRaw, watch, computed } from 'vue'
import { useChatStore } from '../stores/chat'
import { useAnimationSettingsStore } from '../stores/settings/animation'
import { useIdleAnimationScheduler } from '../composables/useIdleAnimationScheduler'
import type { IdleAnimation } from '../composables/idleAnimationCatalog'
import { avatarDebugLog } from '../composables/avatarDebugLog'

/**
 * Live2D-only avatar stage. The previous VRM renderer branch was
 * removed in the "Seren — suppression totale de VRM" chantier — the
 * single-renderer approach keeps the code KISS and lets us double
 * down on Live2D (motion groups, expressions, viseme lipsync, spring
 * physics) without fighting a dynamic 3D rig.
 *
 * The host page supplies `modelUrl` ; when omitted, the bundled
 * Hiyori model is used so first-run users see a working avatar.
 */
const DEFAULT_MODEL_URL = '/avatars/live2d/hiyori/Hiyori.model3.json'

const props = defineProps<{
  /** Optional override. Defaults to the bundled Hiyori model. */
  modelUrl?: string
}>()

const chatStore = useChatStore()
const animationSettings = useAnimationSettingsStore()

const currentEmotion = ref<string>('neutral')
const renderError = ref<string | null>(null)

const activeModelUrl = computed<string>(() => props.modelUrl ?? DEFAULT_MODEL_URL)

// ── Idle animation scheduler ──────────────────────────────────────────
// Kept wired even with an empty catalog so the avatar-state FSM stays
// plugged in for the future "motion group per phase" chantier. Empty
// catalog = no fires (Phase 5 contract).
const idleIsActive = computed(() =>
  !chatStore.isStreaming
  && !chatStore.isThinking
  && chatStore.connectionStatus === 'ready',
)
const idleMood = computed<string | null>(() => chatStore.currentEmotion?.emotion ?? null)
const idleIntervalSeconds = computed<readonly [number, number]>(
  () => animationSettings.idleIntervalSeconds,
)
const idleCatalog: readonly IdleAnimation[] = []

useIdleAnimationScheduler({
  isActive: idleIsActive,
  mood: idleMood,
  intervalSeconds: idleIntervalSeconds,
  enabled: computed(() => animationSettings.idleEnabled),
  catalog: idleCatalog,
  onTrigger: (animation) => {
    avatarDebugLog('idle', 'trigger', { id: animation.id, mood: idleMood.value })
    chatStore.currentAction = { action: animation.id, nonce: Date.now() }
  },
})

// Watch the live `currentEmotion` ref (populated by the `avatar:emotion`
// handler mid-stream, before the assistant message exists in the history).
watch(() => chatStore.currentEmotion?.nonce, () => {
  const payload = chatStore.currentEmotion
  if (payload?.emotion) currentEmotion.value = payload.emotion
})

// Lipsync frames from the store, converted to the format expected by Live2DViewer
const lipsyncFrames = computed(() =>
  chatStore.lipsyncFrames.map(f => ({
    viseme: f.viseme,
    startTime: f.startTime,
    duration: f.duration,
    weight: f.weight,
  })),
)

// Live2D viewer lazy-loaded — shallowRef + markRaw keep the component
// definition out of Vue's reactive graph (mandatory for PIXI internals).
const Live2DViewer = shallowRef<any>(null)

async function loadLive2DViewer(): Promise<void> {
  try {
    const mod = await import('@seren/ui-live2d')
    Live2DViewer.value = markRaw(mod.Live2DViewer)
  }
  catch {
    renderError.value = 'Live2D renderer not available'
  }
}

loadLive2DViewer()
</script>

<template>
  <div class="avatar-stage">
    <div v-if="renderError" class="avatar-stage__error">
      {{ renderError }}
    </div>
    <component
      :is="Live2DViewer"
      v-if="Live2DViewer && activeModelUrl"
      :model-url="activeModelUrl"
      :emotion="currentEmotion"
      :action="chatStore.currentAction"
      :thinking="chatStore.isThinking"
      :lipsync-frames="lipsyncFrames"
    />
    <div v-if="!activeModelUrl" class="avatar-stage__placeholder">
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
