<script setup lang="ts">
import { TresCanvas } from '@tresjs/core'
import { OutlineEffect } from 'three/addons/effects/OutlineEffect.js'
import type { Camera, Object3D, Scene, WebGLRenderer } from 'three'
import { onUnmounted, shallowRef, watch } from 'vue'
import { useVRM } from '../composables/useVRM'

const props = withDefaults(defineProps<{
  modelUrl: string
  emotion?: string
  /** Enable the VRM toon outline effect. Defaults to true. */
  outline?: boolean
  /** Animation clip name to play after the VRM is loaded. Pairs with `animationUrl`. */
  animationName?: string
  animationUrl?: string
}>(), {
  outline: true,
})

const { vrm, isLoading, error, loadVRM, setExpression, dispose } = useVRM()
const outlineEffect = shallowRef<OutlineEffect | null>(null)

watch(() => props.modelUrl, (url) => {
  if (url) loadVRM(url)
}, { immediate: true })

watch(() => props.emotion, (emotion) => {
  if (emotion) setExpression(emotion)
})

/**
 * TresJS exposes a hook `@ready` on TresCanvas that hands back the
 * underlying WebGLRenderer. We wrap it in an OutlineEffect so the cel-shaded
 * toon rendering mandated by AIRI docs 5.3 applies to the VRM. The outline
 * effect monkey-patches renderer.render, so we just need to keep a
 * reference alive — TresJS will call it internally every frame.
 */
function handleReady(ctx: { renderer: WebGLRenderer }): void {
  if (!props.outline) return
  outlineEffect.value = new OutlineEffect(ctx.renderer, {
    defaultThickness: 0.003,
    defaultColor: [0, 0, 0],
    defaultAlpha: 0.8,
    defaultKeepAlive: true,
  })
  // Patch renderer.render to go through the outline effect.
  const originalRender = ctx.renderer.render.bind(ctx.renderer)
  ctx.renderer.render = (scene: Object3D, camera: Camera) => {
    const effect = outlineEffect.value
    if (effect) {
      effect.render(scene as Scene, camera)
    }
    else {
      originalRender(scene as Scene, camera)
    }
  }
}

onUnmounted(() => {
  outlineEffect.value = null
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
  color: #ef4444;
}
</style>
