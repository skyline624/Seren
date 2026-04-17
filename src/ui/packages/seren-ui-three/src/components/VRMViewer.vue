<script setup lang="ts">
import { TresCanvas } from '@tresjs/core'
import type { WebGLRenderer } from 'three'
import { onUnmounted, ref, watch } from 'vue'
import { useVRM } from '../composables/useVRM'
import { useVRMAnimation } from '../composables/useVRMAnimation'
import { useLipsync, type VisemeTrackFrame } from '../composables/useLipsync'
import { isProceduralGesture, useVRMGestures } from '../composables/useVRMGestures'

const props = withDefaults(defineProps<{
  modelUrl: string
  emotion?: string
  /** Enable the VRM toon outline effect. Defaults to true. */
  outline?: boolean
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
}>(), {
  outline: true,
  idleAnimationUrl: '/idle_loop.vrma',
  emotionHoldMs: 3000,
})

const { vrm, isLoading, error, loadVRM, setExpression, onTick, onTickOverride, dispose } = useVRM()
const animation = useVRMAnimation()
const lipsync = useLipsync(() => vrm.value)
const gestures = useVRMGestures(() => vrm.value)
// Procedural gestures must write AFTER the mixer (which plays the idle
// clip) so their rotations aren't overwritten each frame.
onTickOverride(gestures.applyOverride)

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

  // Prefer a .vrma clip when the host app supplies one — richer motion
  // data than procedural gestures can offer. Fall back to procedural
  // humanoid-bone animations so the avatar always reacts visually to
  // `<action:NAME>` markers, even without bundled assets.
  const clipUrl = props.actionClipMap?.[action]
  if (clipUrl) {
    const ready = await ensureClip(action, clipUrl)
    if (!ready) return
    animation.play(action, 0.2)
    if (actionReturnTimer) clearTimeout(actionReturnTimer)
    // Best-effort clip duration lookup; fall back to emotionHoldMs.
    actionReturnTimer = setTimeout(() => {
      animation.play('idle', 0.3)
    }, props.emotionHoldMs)
    return
  }

  if (isProceduralGesture(action)) {
    gestures.play(action)
  }
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

// OutlineEffect is disabled — incompatible with Three.js v0.183+
// (renderer.render API changed, causes "Cannot read properties of undefined (reading 'bind')")
// TODO: re-enable when three/addons/effects/OutlineEffect is updated
function handleReady(_ctx: { renderer: WebGLRenderer }): void {
  // no-op for now
}

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
    <TresCanvas v-if="vrm" window-size @ready="handleReady">
      <TresPerspectiveCamera :position="[0, 1.3, 1.5]" :look-at="[0, 1, 0]" />
      <TresAmbientLight :intensity="0.6" />
      <TresDirectionalLight :position="[1, 2, 1]" :intensity="0.8" />
      <primitive v-if="vrm" :object="vrm.scene" />
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
