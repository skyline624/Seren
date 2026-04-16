<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { useLive2D } from '../composables/useLive2D'

const props = defineProps<{
  modelUrl: string
  emotion?: string
}>()

const canvasRef = ref<HTMLCanvasElement>()
const { isLoading, error, init, loadModel, setEmotion, dispose } = useLive2D()

onMounted(async () => {
  await init(canvasRef.value)
  if (props.modelUrl) loadModel(props.modelUrl)
})

watch(() => props.modelUrl, (url) => {
  if (url) loadModel(url)
})

watch(() => props.emotion, (emotion) => {
  if (emotion) setEmotion(emotion)
})

onUnmounted(() => dispose())
</script>

<template>
  <div class="live2d-viewer">
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