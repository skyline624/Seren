<script setup lang="ts">
import { TresCanvas } from '@tresjs/core'
import { computed, onUnmounted, ref, watch, watchEffect } from 'vue'
import { useVRM } from '../composables/useVRM'
import { useVRMAnimation } from '../composables/useVRMAnimation'
import { useLipsync, type VisemeTrackFrame } from '../composables/useLipsync'
import { type EyeTrackingMode } from '../composables/useVRMLookAt'
import VRMLookAtController from './VRMLookAtController.vue'
import VRMOutlinePass from './VRMOutlinePass.vue'

const props = withDefaults(defineProps<{
  modelUrl: string
  emotion?: string
  /** Enable the VRM toon outline effect. Defaults to true. */
  outline?: boolean
  /** Outline stroke thickness (world units). */
  outlineThickness?: number
  /** Outline color as an [r,g,b] array in 0..1. */
  outlineColor?: [number, number, number]
  /** Outline alpha in 0..1. */
  outlineAlpha?: number
  /** URL to an idle animation (.vrma). Defaults to /idle_loop.vrma. */
  idleAnimationUrl?: string
  /** Lipsync viseme frames to schedule on the VRM mouth blendshapes. */
  lipsyncFrames?: VisemeTrackFrame[]
  /**
   * Optional mapping from emotion name → .vrma clip URL. When the active
   * emotion has a matching clip, it crossfades in for `emotionHoldMs` then
   * returns to idle. Unmapped emotions still drive the facial expression
   * (via `useVRM.setExpression`) — only body motion is gated by this map.
   */
  emotionClipMap?: Record<string, string>
  /**
   * Optional mapping from action name (wave, nod, bow, think, …) → .vrma
   * clip URL. When an action event arrives, the matching clip is played
   * once and the mixer returns to idle after the clip duration.
   */
  actionClipMap?: Record<string, string>
  /** Most recent action fired by the hub. The `nonce` is bumped on every
   * broadcast so repeat gestures re-trigger the watcher. */
  action?: { action: string, nonce: number } | null
  /** True while the LLM is streaming its chain-of-thought. Drives a
   * "thinking" clip or a fallback parametric head-tilt. */
  thinking?: boolean
  /** How long (ms) an emotion clip stays active before returning to idle. */
  emotionHoldMs?: number
  /** Uniform scale applied to the VRM scene. Defaults to 1. */
  modelScale?: number
  /** Y offset applied to the VRM scene. Defaults to 0. */
  positionY?: number
  /** Explicit Y rotation (radians). `null` → auto-detect per VRM version. */
  rotationY?: number | null
  /** Camera horizontal distance from the model (meters). */
  cameraDistance?: number
  /** Camera + lookAt height (meters). */
  cameraHeight?: number
  /** Camera field of view in degrees. */
  cameraFov?: number
  /** Ambient light intensity. */
  ambientIntensity?: number
  /** Directional light intensity. */
  directionalIntensity?: number
  /** Eye tracking mode: camera / pointer / off. */
  eyeTrackingMode?: EyeTrackingMode
}>(), {
  outline: true,
  outlineThickness: 0.003,
  outlineColor: () => [0, 0, 0] as [number, number, number],
  outlineAlpha: 0.8,
  idleAnimationUrl: '/idle_loop.vrma',
  emotionHoldMs: 3000,
  modelScale: 1,
  positionY: 0,
  rotationY: null,
  cameraDistance: 1.5,
  cameraHeight: 1.3,
  cameraFov: 50,
  ambientIntensity: 0.6,
  directionalIntensity: 0.8,
  eyeTrackingMode: 'camera',
})

const {
  vrm,
  isLoading,
  error,
  loadVRM,
  autoRotationY,
  setExpression,
  onTick,
  dispose,
} = useVRM()
const animation = useVRMAnimation()
const lipsync = useLipsync(() => vrm.value)

// Reactive camera position derived from the distance/height settings.
// `TresPerspectiveCamera :position` binds to this so user slider moves
// reflect immediately without reloading the scene.
const cameraPos = computed<[number, number, number]>(() => [
  0,
  props.cameraHeight,
  props.cameraDistance,
])
const lookAtPos = computed<[number, number, number]>(() => [0, props.cameraHeight, 0])

// Scale / position / rotation are applied to `vrm.value.scene` whenever
// either the VRM or the user-controlled value changes. `rotationY === null`
// means "auto" → fall back on useVRM's version-aware default (π for VRM
// 0.x, 0 for VRM 1.0+).
watchEffect(() => {
  if (!vrm.value) return
  vrm.value.scene.scale.setScalar(props.modelScale)
  vrm.value.scene.position.y = props.positionY
  vrm.value.scene.rotation.y = props.rotationY ?? autoRotationY()
})

// Eye tracking is driven by `VRMLookAtController`, a child of
// <TresCanvas> that reads the scene camera from `useTresContext()`.
// Mounting it inside the canvas guarantees the camera ref is always
// the live unwrapped Camera — never the underlying ComputedRef —
// which removes the VRMLookAt.update() crash we had when the ref
// slipped through the @ready fallback.

// Track the set of clip names we've already asked the mixer to load so
// repeat emotions don't trigger duplicate fetches. `useVRMAnimation`
// itself caches the AnimationAction, but loadClip() would still refetch
// the .vrma if we called it twice.
const loadedClips = new Set<string>(['idle'])

let emotionReturnTimer: ReturnType<typeof setTimeout> | null = null
let actionReturnTimer: ReturnType<typeof setTimeout> | null = null

// Parametric head-tilt fallback when the user didn't supply a thinking
// clip. Amplitude intentionally subtle — the goal is a hint, not a bow.
const thinkingTiltRad = ref(0)
const THINKING_TILT_MAX = -0.15

// Load VRM model when URL changes
watch(() => props.modelUrl, (url) => {
  if (url) loadVRM(url)
}, { immediate: true })

// When VRM is loaded, attach animation mixer and load idle clip
watch(vrm, async (loadedVrm) => {
  if (!loadedVrm) return

  animation.attach(loadedVrm)
  loadedClips.clear()
  loadedClips.add('idle')

  onTick((delta) => {
    animation.update(delta)
    // Apply parametric thinking tilt if no clip is driving it.
    const neck = loadedVrm.humanoid?.getNormalizedBoneNode('neck')
    if (neck) {
      // Ease towards the target tilt so the transition isn't abrupt.
      neck.rotation.x += (thinkingTiltRad.value - neck.rotation.x) * Math.min(1, delta * 4)
    }
  })

  if (props.idleAnimationUrl) {
    await animation.loadClip('idle', props.idleAnimationUrl, loadedVrm)
    animation.play('idle')
  }
})

async function ensureClip(name: string, url: string): Promise<boolean> {
  if (!vrm.value) return false
  if (loadedClips.has(name)) return true
  await animation.loadClip(name, url, vrm.value)
  loadedClips.add(name)
  return true
}

watch(() => props.emotion, async (emotion) => {
  if (emotion) setExpression(emotion)

  const clipUrl = emotion ? props.emotionClipMap?.[emotion] : undefined
  if (!clipUrl || !emotion) return

  const ready = await ensureClip(emotion, clipUrl)
  if (!ready) return

  animation.play(emotion, 0.3)
  if (emotionReturnTimer) clearTimeout(emotionReturnTimer)
  emotionReturnTimer = setTimeout(() => {
    animation.play('idle', 0.3)
  }, props.emotionHoldMs)
})

watch(() => props.action?.nonce, async () => {
  const action = props.action?.action
  if (!action) return

  // Action → .vrma clip. Single-path architecture: if the host app
  // didn't register a clip for this action, we no-op rather than
  // falling back to procedural bone manipulation (which has proved
  // too model-specific to ship reliably). Missing clips are silent —
  // callers observe via the scheduler's debug log.
  const clipUrl = props.actionClipMap?.[action]
  if (!clipUrl) return
  const ready = await ensureClip(action, clipUrl)
  if (!ready) return
  animation.play(action, 0.2)
  if (actionReturnTimer) clearTimeout(actionReturnTimer)
  actionReturnTimer = setTimeout(() => {
    animation.play('idle', 0.3)
  }, props.emotionHoldMs)
})

watch(() => props.thinking, async (thinking) => {
  if (thinking) {
    const clipUrl = props.actionClipMap?.think
    if (clipUrl) {
      const ready = await ensureClip('think', clipUrl)
      if (ready) animation.play('think', 0.3)
    }
    else {
      // Parametric fallback: tilt the head forward slightly.
      thinkingTiltRad.value = THINKING_TILT_MAX
    }
  }
  else {
    thinkingTiltRad.value = 0
    // Return to idle if we were on a think clip.
    if (loadedClips.has('think')) animation.play('idle', 0.3)
  }
})

watch(() => props.lipsyncFrames, (frames) => {
  if (frames && frames.length > 0) {
    lipsync.playTrack(frames)
  }
})

onUnmounted(() => {
  if (emotionReturnTimer) clearTimeout(emotionReturnTimer)
  if (actionReturnTimer) clearTimeout(actionReturnTimer)
  lipsync.stop()
  animation.dispose()
  dispose()
})
</script>

<template>
  <div class="vrm-viewer">
    <div v-if="isLoading" class="vrm-viewer__loading">
      Loading avatar...
    </div>
    <div v-if="error" class="vrm-viewer__error">
      {{ error }}
    </div>
    <TresCanvas v-if="vrm" window-size>
      <TresPerspectiveCamera :position="cameraPos" :look-at="lookAtPos" :fov="cameraFov" />
      <TresAmbientLight :intensity="ambientIntensity" />
      <TresDirectionalLight :position="[1, 2, 1]" :intensity="directionalIntensity" />
      <primitive v-if="vrm" :object="vrm.scene" />
      <VRMOutlinePass
        v-if="outline"
        :thickness="outlineThickness"
        :color="outlineColor"
        :alpha="outlineAlpha"
      />
      <VRMLookAtController :vrm="vrm" :mode="eyeTrackingMode" />
    </TresCanvas>
  </div>
</template>

<style scoped>
.vrm-viewer {
  width: 100%;
  height: 100%;
  position: relative;
}
.vrm-viewer__loading,
.vrm-viewer__error {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #64748b;
  font-size: 0.875rem;
}
.vrm-viewer__error {
  color: #fca5a5;
}
</style>
