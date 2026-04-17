<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { useLive2D } from '../composables/useLive2D'
import { useLive2DLipsync, type VisemeTrackFrame } from '../composables/useLive2DLipsync'

const props = defineProps<{
  modelUrl: string
  emotion?: string
  /** Viseme track scheduled on the Cubism 4 mouth parameters. */
  lipsyncFrames?: VisemeTrackFrame[]
  /** Last avatar action broadcast by the hub. Nonce re-triggers repeats. */
  action?: { action: string, nonce: number } | null
  /** True while the LLM is streaming its chain-of-thought. */
  thinking?: boolean
}>()

const wrapperRef = ref<HTMLDivElement>()
const canvasRef = ref<HTMLCanvasElement>()
const { model, isLoading, error, init, loadModel, setEmotion, dispose } = useLive2D()
const lipsync = useLive2DLipsync(() => model.value)

/**
 * Map an action name to a Cubism motion group + index. Hiyori ships with
 * generic motion groups; we route actions through `Idle` by default and
 * expose `TapBody` for more energetic gestures. Unknown actions fall back
 * to expression-only feedback via `setEmotion`.
 */
const ACTION_MOTION_MAP: Record<string, { group: string, index?: number }> = {
  wave: { group: 'TapBody', index: 0 },
  nod: { group: 'Idle', index: 0 },
  bow: { group: 'TapBody', index: 1 },
  think: { group: 'Idle', index: 1 },
}

onMounted(async () => {
  // Size the canvas backing store to the wrapper so the model isn't rendered
  // into a small 400×600 bitmap and then scaled up by CSS.
  const wrapper = wrapperRef.value
  const canvas = canvasRef.value
  if (wrapper && canvas) {
    canvas.width = wrapper.clientWidth || 400
    canvas.height = wrapper.clientHeight || 600
  }
  await init(canvas, {
    width: canvas?.width ?? 400,
    height: canvas?.height ?? 600,
  })
  if (props.modelUrl) loadModel(props.modelUrl)
})

watch(() => props.modelUrl, (url) => {
  if (url) loadModel(url)
})

watch(() => props.emotion, (emotion) => {
  if (emotion) setEmotion(emotion)
})

watch(() => props.lipsyncFrames, (frames) => {
  if (frames && frames.length > 0) lipsync.playTrack(frames)
})

watch(() => props.action?.nonce, () => {
  const name = props.action?.action
  if (!name || !model.value) return
  const mapping = ACTION_MOTION_MAP[name]
  if (mapping) {
    model.value.motion(mapping.group, mapping.index ?? 0)
  }
  else {
    // Unknown action: best-effort expression feedback so the avatar reacts.
    setEmotion(name)
  }
})

watch(() => props.thinking, (thinking) => {
  const m = model.value
  if (!m) return
  if (thinking) {
    const mapping = ACTION_MOTION_MAP.think
    if (mapping) m.motion(mapping.group, mapping.index ?? 0)
  }
})

onUnmounted(() => {
  lipsync.stop()
  dispose()
})
</script>

<template>
  <div ref="wrapperRef" class="live2d-viewer">
    <div v-if="isLoading" class="live2d-viewer__loading">Loading avatar...</div>
    <div v-if="error" class="live2d-viewer__error">{{ error }}</div>
    <canvas ref="canvasRef" class="live2d-viewer__canvas" />
  </div>
</template>

<style scoped>
.live2d-viewer {
  width: 100%;
  height: 100%;
  position: relative;
}
.live2d-viewer__canvas {
  width: 100%;
  height: 100%;
}
.live2d-viewer__loading,
.live2d-viewer__error {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #64748b;
  font-size: 0.875rem;
}
.live2d-viewer__error {
  color: #ef4444;
}
</style>