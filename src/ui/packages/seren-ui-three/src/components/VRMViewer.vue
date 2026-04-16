<script setup lang="ts">
import { TresCanvas } from '@tresjs/core'
import type { WebGLRenderer } from 'three'
import { onUnmounted, watch } from 'vue'
import { useVRM } from '../composables/useVRM'
import { useVRMAnimation } from '../composables/useVRMAnimation'
import { useLipsync, type VisemeTrackFrame } from '../composables/useLipsync'

const props = withDefaults(defineProps<{
  modelUrl: string
  emotion?: string
  /** Enable the VRM toon outline effect. Defaults to true. */
  outline?: boolean
  /** URL to an idle animation (.vrma). Defaults to /idle_loop.vrma. */
  idleAnimationUrl?: string
  /** Lipsync viseme frames to schedule on the VRM mouth blendshapes. */
  lipsyncFrames?: VisemeTrackFrame[]
}>(), {
  outline: true,
  idleAnimationUrl: '/idle_loop.vrma',
})

const { vrm, isLoading, error, loadVRM, setExpression, onTick, dispose } = useVRM()
const animation = useVRMAnimation()
const lipsync = useLipsync(() => vrm.value)

// Load VRM model when URL changes
watch(() => props.modelUrl, (url) => {
  if (url) loadVRM(url)
}, { immediate: true })

// When VRM is loaded, attach animation mixer and load idle clip
watch(vrm, async (loadedVrm) => {
  if (!loadedVrm) return

  animation.attach(loadedVrm)
  onTick(delta => animation.update(delta))

  if (props.idleAnimationUrl) {
    await animation.loadClip('idle', props.idleAnimationUrl, loadedVrm)
    animation.play('idle')
  }
})

watch(() => props.emotion, (emotion) => {
  if (emotion) setExpression(emotion)
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
      <primitive :object="vrm!.scene" />
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
