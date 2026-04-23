<script setup lang="ts">
import { TresCanvas } from '@tresjs/core'
import { computed, onUnmounted, watch, watchEffect } from 'vue'
import { Vector3 } from 'three'
import { useVRM } from '../composables/useVRM'
import { useVRMAnimation } from '../composables/useVRMAnimation'
import { useLipsync, type VisemeTrackFrame } from '../composables/useLipsync'
import { useBlink } from '../composables/useBlink'
import { useIdleBodySway } from '../composables/useIdleBodySway'
import { useIdleEyeSaccades } from '../composables/useIdleEyeSaccades'
import { useVRMEmote } from '../composables/useVRMEmote'
import { type EyeTrackingMode } from '../composables/useVRMLookAt'
import VRMLookAtController from './VRMLookAtController.vue'
import VRMOutlinePass from './VRMOutlinePass.vue'

const props = withDefaults(defineProps<{
  modelUrl: string
  emotion?: string
  /** Strength of the emotion blend in [0, 1]. Explicit markers
   *  typically pass 1.0 ; text-classifier predictions pass their
   *  confidence score so subtle inferences produce subtle faces. */
  emotionIntensity?: number
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
  /** Procedural face: auto-blink. Default `true`. */
  blinkEnabled?: boolean
  /** Procedural face: eye saccades (idle micro-movements). Default `true`.
   *  Effective only when `eyeTrackingMode === 'off'` (otherwise the
   *  scene-driven gaze target owns the lookAt). */
  saccadeEnabled?: boolean
  /** Procedural body sway (breath + weight shift + hip sway) composed
   *  on top of the base idle clip. Default `true`. */
  bodySwayEnabled?: boolean
  /**
   * Multipliers applied to every procedural layer based on the
   * avatar's current "phase" (idle / listening / thinking / talking /
   * reactive). Driven by `useAvatarStateStore.gains` in Phase 5. When
   * omitted, every layer runs at its baseline (gain 1, headTilt 0).
   * Values :
   *   - bodySway : amplitude multiplier on spine/chest/hips sinusoids.
   *   - blink    : frequency multiplier (higher = more blinks).
   *   - saccade  : frequency multiplier (higher = fidgetier eyes).
   *   - headTilt : absolute X-rotation offset on `neck` (radians).
   */
  layerGains?: {
    bodySway: number
    blink: number
    saccade: number
    headTilt: number
  }
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
  blinkEnabled: true,
  saccadeEnabled: true,
  bodySwayEnabled: true,
  emotionIntensity: 1,
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

// ── Face layer (Phase 1) ─────────────────────────────────────────────
// Blink + saccade + emote compose INDEPENDENTLY of the body mixer. They
// write to VRMExpressionManager and vrm.lookAt.target respectively ;
// vrm.update(delta) applies everything on the same frame (three-vrm
// canonical pattern). Only the saccade defers to the scene-driven look-
// at controller when the user has eye tracking on `camera`/`pointer` —
// in `off` mode, the saccade takes over the target.
const blink = useBlink({
  gainRef: () => props.layerGains?.blink ?? 1,
})
const saccadeAnchor = computed(() => new Vector3(0, props.cameraHeight, 1))
const saccade = useIdleEyeSaccades({
  activeRef: () => props.saccadeEnabled && props.eyeTrackingMode === 'off',
  gainRef: () => props.layerGains?.saccade ?? 1,
})
const emote = useVRMEmote(() => vrm.value)
const bodySway = useIdleBodySway({
  enabledRef: () => props.bodySwayEnabled,
  gainRef: () => props.layerGains?.bodySway ?? 1,
})

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

// ── Action queue (Phase 3) ───────────────────────────────────────────
// FIFO of pending action clips. When the current action is still
// playing, subsequent ones wait their turn instead of chain-cutting
// each other after `emotionHoldMs`. Bounded depth prevents queue
// bombs from a spammy marker stream.
const ACTION_QUEUE_MAX = 3
const actionQueue: string[] = []
let currentActionName: string | null = null
// Extra fade time after each clip finishes before we crossfade back
// to idle — avoids a visible pop on clips whose final frame isn't
// quite neutral.
const ACTION_RETURN_PAD_MS = 150

// Parametric head-tilt. Driven by the avatar-state FSM (Phase 5) via
// `layerGains.headTilt` when provided — otherwise falls back to the
// boolean `props.thinking` for renderers that don't plumb the FSM yet.
const THINKING_TILT_MAX = -0.15
const thinkingTiltRad = computed<number>(() => {
  if (props.layerGains) return props.layerGains.headTilt
  return props.thinking ? THINKING_TILT_MAX : 0
})

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
    // Canonical three-vrm tick order :
    //  1. mixer — base idle clip writes node.quaternion
    //  2. body sway — quaternion.multiply on spine/chest/hips (additive)
    //  3. emote  — blends VRMExpressionManager values
    //  4. blink  — writes 'blink' expression (gated by blinkEnabled)
    //  5. saccade — moves vrm.lookAt.target (only in 'off' mode + enabled)
    //  6. vrm.update(delta) — runs internally by useVRM after this
    //     callback, applies humanoid + lookAt + expressions + spring bones.
    animation.update(delta)
    bodySway.update(loadedVrm, delta)
    emote.update(delta)
    if (props.blinkEnabled) {
      blink.update(loadedVrm, delta)
    }
    saccade.update(loadedVrm, saccadeAnchor.value, delta)

    // Parametric thinking tilt — lerped toward the current target.
    const neck = loadedVrm.humanoid?.getNormalizedBoneNode('neck')
    if (neck) {
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
  if (emotion) {
    // New path (Phase 1+4) : route through the blendshape lerp manager.
    // Unknown emotion names fall back to neutral inside useVRMEmote,
    // and the composable auto-schedules a reset to neutral after
    // `emotionHoldMs`. Intensity is forwarded so the text-classifier's
    // confidence score can produce subtler faces than LLM markers.
    emote.setEmotionWithResetAfter(emotion, props.emotionHoldMs, props.emotionIntensity)
    // Legacy path kept for backward compat while setExpression is
    // still useful to callers driving non-emotion presets directly.
    // eslint-disable-next-line deprecation/deprecation
    setExpression(emotion)
  }

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

/**
 * Play `name` now, schedule the return-to-next (dequeue or idle)
 * based on the clip's real duration rather than a fixed cap. Falls
 * back to `emotionHoldMs` when the clip duration is unknown.
 */
async function playActionNow(name: string): Promise<void> {
  const clipUrl = props.actionClipMap?.[name]
  if (!clipUrl) return
  const ready = await ensureClip(name, clipUrl)
  if (!ready) return

  animation.play(name, 0.2)
  currentActionName = name

  if (actionReturnTimer) clearTimeout(actionReturnTimer)
  const clipDurationSec = animation.getClipDuration(name)
  const returnMs = clipDurationSec !== null
    ? Math.round(clipDurationSec * 1000) + ACTION_RETURN_PAD_MS
    : props.emotionHoldMs
  actionReturnTimer = setTimeout(onActionFinished, returnMs)
}

/**
 * Timer callback : drain the next queued action, or return to idle
 * when the queue is empty.
 */
function onActionFinished(): void {
  currentActionName = null
  actionReturnTimer = null
  const next = actionQueue.shift()
  if (next !== undefined) {
    void playActionNow(next)
    return
  }
  animation.play('idle', 0.3)
}

/**
 * Drop all pending actions and cancel the return timer — called
 * when the avatar state transitions out of `idle` / `reactive`
 * (Phase 5). Keeps the currently-playing clip running : only the
 * queued follow-ups are cleared.
 */
function drainActionQueue(): void {
  actionQueue.length = 0
}

watch(() => props.action?.nonce, () => {
  const action = props.action?.action
  if (!action) return

  // Action → .vrma clip. Single-path architecture: if the host app
  // didn't register a clip for this action, we no-op (callers observe
  // via the scheduler's debug log — no procedural fallback).
  if (!props.actionClipMap?.[action]) return

  if (currentActionName !== null) {
    // Busy : queue the request up to the bounded depth.
    if (actionQueue.length < ACTION_QUEUE_MAX) {
      actionQueue.push(action)
    }
    // else : silent drop — spammy marker stream protection.
    return
  }
  void playActionNow(action)
})

// Expose `drainActionQueue` as a template-ref method once Phase 5
// wires the state machine. Kept as an internal function here to
// avoid a public prop-drill before the consumer exists.
void drainActionQueue  // no-op reference — prevents unused-var lint.

watch(() => props.thinking, async (thinking) => {
  if (thinking) {
    // Only the clip side still fires here — the parametric head-tilt
    // is now reactive on `layerGains.headTilt` (or `props.thinking`
    // fallback) via the `thinkingTiltRad` computed above.
    const clipUrl = props.actionClipMap?.think
    if (clipUrl) {
      const ready = await ensureClip('think', clipUrl)
      if (ready) animation.play('think', 0.3)
    }
  }
  else if (loadedClips.has('think')) {
    // Return to idle if we were on a think clip.
    animation.play('idle', 0.3)
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
  emote.dispose()
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
