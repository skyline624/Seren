<script setup lang="ts">
import { onTresReady, useLoop, useTresContext } from '@tresjs/core'
import { OutlineEffect } from 'three/addons/effects/OutlineEffect.js'
import { onBeforeUnmount } from 'vue'

// Toon outline pass. Must be mounted as a child of <TresCanvas> so that
// useTresContext() can resolve the renderer/scene/camera via provide/inject.
// Registering a callback on loop.render() replaces TresJS's default render
// call, so we route the scene through OutlineEffect every frame instead.

const props = withDefaults(defineProps<{
  thickness?: number
  color?: [number, number, number]
  alpha?: number
}>(), {
  thickness: 0.003,
  color: () => [0, 0, 0],
  alpha: 0.8,
})

const { renderer } = useTresContext()
const { render } = useLoop()

let effect: OutlineEffect | null = null
let off: (() => void) | null = null

onTresReady(() => {
  if (!renderer.value)
    return
  effect = new OutlineEffect(renderer.value, {
    defaultThickness: props.thickness,
    defaultColor: [...props.color],
    defaultAlpha: props.alpha,
  })
  const handle = render(({ scene, camera }) => {
    effect?.render(scene, camera)
  })
  off = handle.off
})

onBeforeUnmount(() => {
  off?.()
  effect = null
})
</script>

<template>
  <slot />
</template>
