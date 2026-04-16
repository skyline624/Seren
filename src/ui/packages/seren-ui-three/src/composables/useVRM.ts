import type { GLTFParser } from 'three/addons/loaders/GLTFLoader.js'
import { VRM, VRMExpressionPresetName, VRMLoaderPlugin } from '@pixiv/three-vrm'
import { Clock, Mesh } from 'three'
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js'
import { onBeforeUnmount, ref, shallowRef } from 'vue'

/**
 * Core VRM composable. Loads a VRM from a URL, exposes an emotion setter
 * mapped to VRM expression presets, and drives a requestAnimationFrame
 * loop that updates SpringBone physics and a user-supplied
 * `onTick(delta)` callback (typically consumed by useVRMAnimation).
 */
export function useVRM() {
  const vrm = shallowRef<VRM | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const currentEmotion = ref<string>('neutral')

  const clock = new Clock()
  let rafHandle = 0
  let tickCallback: ((delta: number) => void) | null = null

  async function loadVRM(url: string): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      const loader = new GLTFLoader()
      loader.register((parser: GLTFParser) => new VRMLoaderPlugin(parser))
      const gltf = await loader.loadAsync(url)
      const loadedVRM = gltf.userData.vrm as VRM
      // VRM 0.x models face +Z by default (non-glTF convention) — rotate them 180°
      // so they face the camera (which looks along -Z). VRM 1.0 already follows the
      // glTF standard (-Z forward) so no rotation is applied.
      const isVRM0 = (loadedVRM.meta as { metaVersion?: string })?.metaVersion === '0'
      loadedVRM.scene.rotation.y = isVRM0 ? Math.PI : 0
      vrm.value = loadedVRM
      startRenderLoop()
    }
    catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load VRM'
    }
    finally {
      isLoading.value = false
    }
  }

  /**
   * Registers (or clears) an extra tick callback. useVRMAnimation calls
   * this with its `update` function so that mixer advancement runs on
   * the same rAF loop as the VRM physics — single source of truth for
   * elapsed time.
   */
  function onTick(cb: ((delta: number) => void) | null): void {
    tickCallback = cb
  }

  function startRenderLoop(): void {
    if (rafHandle !== 0 || typeof requestAnimationFrame === 'undefined') {
      return
    }
    clock.start()
    const tick = (): void => {
      rafHandle = requestAnimationFrame(tick)
      const delta = clock.getDelta()
      tickCallback?.(delta)
      vrm.value?.update(delta)
    }
    rafHandle = requestAnimationFrame(tick)
  }

  function stopRenderLoop(): void {
    if (rafHandle !== 0 && typeof cancelAnimationFrame !== 'undefined') {
      cancelAnimationFrame(rafHandle)
    }
    rafHandle = 0
    clock.stop()
  }

  function setExpression(emotion: string, weight = 1.0): void {
    if (!vrm.value)
      return

    const manager = vrm.value.expressionManager
    if (!manager)
      return

    // Map emotion names to VRM preset names. Mouth presets (Aa/Ih/Ou/Ee/Oh)
    // are deliberately excluded — they are driven by the lipsync composable.
    const emotionMap: Record<string, string> = {
      joy: VRMExpressionPresetName.Happy,
      happy: VRMExpressionPresetName.Happy,
      anger: VRMExpressionPresetName.Angry,
      angry: VRMExpressionPresetName.Angry,
      sorrow: VRMExpressionPresetName.Sad,
      sad: VRMExpressionPresetName.Sad,
      surprise: VRMExpressionPresetName.Surprised,
      surprised: VRMExpressionPresetName.Surprised,
      relaxed: VRMExpressionPresetName.Relaxed,
      neutral: VRMExpressionPresetName.Neutral,
    }

    // Reset only non-mouth presets so lipsync weights are preserved.
    const nonMouthPresets = [
      VRMExpressionPresetName.Happy,
      VRMExpressionPresetName.Angry,
      VRMExpressionPresetName.Sad,
      VRMExpressionPresetName.Surprised,
      VRMExpressionPresetName.Relaxed,
      VRMExpressionPresetName.Neutral,
      VRMExpressionPresetName.Blink,
      VRMExpressionPresetName.BlinkLeft,
      VRMExpressionPresetName.BlinkRight,
    ]
    for (const preset of nonMouthPresets) {
      manager.setValue(preset, 0)
    }

    const vrmExpression = emotionMap[emotion.toLowerCase()] ?? emotion
    manager.setValue(vrmExpression, weight)
    currentEmotion.value = emotion
  }

  function resetExpression(): void {
    if (!vrm.value?.expressionManager)
      return
    for (const preset of Object.values(VRMExpressionPresetName)) {
      vrm.value.expressionManager.setValue(preset, 0)
    }
    currentEmotion.value = 'neutral'
  }

  function dispose(): void {
    stopRenderLoop()
    if (vrm.value) {
      vrm.value.scene.traverse((child) => {
        if (child instanceof Mesh) {
          child.geometry.dispose()
          if (Array.isArray(child.material)) {
            child.material.forEach(m => m.dispose())
          }
          else {
            child.material.dispose()
          }
        }
      })
      vrm.value = null
    }
  }

  onBeforeUnmount(() => dispose())

  return {
    vrm,
    isLoading,
    error,
    currentEmotion,
    loadVRM,
    setExpression,
    resetExpression,
    onTick,
    dispose,
  }
}
