<script setup lang="ts">
import type { VRM } from '@pixiv/three-vrm'
import { useTresContext } from '@tresjs/core'
import { useVRMLookAt, type EyeTrackingMode } from '../composables/useVRMLookAt'

// Binds the VRM eye-tracking composable to the active TresJS scene
// camera. Must be rendered as a child of <TresCanvas> so that
// useTresContext() can resolve the provided camera + renderer refs.
//
// The previous implementation intercepted the TresCanvas @ready event
// from the parent and unwrapped the camera manually; that fallback
// silently stored the raw ComputedRef when the camera wasn't ready yet
// at emit time, which VRMLookAt.update() then tried to call
// getWorldPosition on — triggering ~60 Hz exceptions. This wrapper
// reads the camera via the same Tres context channel that
// VRMOutlinePass already relies on, so the value is always either a
// real THREE.Camera or undefined — never a Vue ref.

const props = defineProps<{
  vrm: VRM | null
  mode: EyeTrackingMode
}>()

const { camera, renderer } = useTresContext()

useVRMLookAt(
  () => props.vrm,
  () => props.mode,
  () => camera.value ?? null,
  () => renderer.value?.domElement ?? null,
)
</script>

<template>
  <slot />
</template>
