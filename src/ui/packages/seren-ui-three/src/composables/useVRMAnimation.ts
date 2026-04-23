import type { VRM } from '@pixiv/three-vrm'
import type { VRMAnimation } from '@pixiv/three-vrm-animation'
import { createVRMAnimationClip, VRMAnimationLoaderPlugin } from '@pixiv/three-vrm-animation'
import { AnimationMixer, type AnimationAction } from 'three'
import { GLTFLoader, type GLTFParser } from 'three/addons/loaders/GLTFLoader.js'
import { ref, shallowRef } from 'vue'

/**
 * Wraps a Three.js `AnimationMixer` around a loaded VRM to play
 * `.vrma` animation files. Supports named tracks with crossfading so
 * the avatar can switch from "idle" to "talking" to "greeting" without
 * a visual pop.
 *
 * The caller is responsible for driving `update(delta)` from a
 * requestAnimationFrame loop — typically done inside `useVRM.ts` which
 * already owns the render tick.
 */
export function useVRMAnimation() {
  const mixer = shallowRef<AnimationMixer | null>(null)
  const currentAction = shallowRef<AnimationAction | null>(null)
  const clips = new Map<string, AnimationAction>()
  const error = ref<string | null>(null)

  /**
   * Attaches the mixer to the loaded VRM. Must be called once the VRM
   * is ready; replays of `attach` with a new VRM wipe the clip cache.
   */
  function attach(vrm: VRM): void {
    stop()
    clips.clear()
    mixer.value = new AnimationMixer(vrm.scene)
  }

  /**
   * Loads a `.vrma` file and stores the resulting AnimationAction under
   * the given name so it can later be played by `play(name)`.
   */
  async function loadClip(name: string, url: string, vrm: VRM): Promise<void> {
    if (!mixer.value) {
      error.value = 'useVRMAnimation.loadClip called before attach'
      return
    }

    try {
      const loader = new GLTFLoader()
      loader.register((parser: GLTFParser) => new VRMAnimationLoaderPlugin(parser))
      const gltf = await loader.loadAsync(url)
      const animations = gltf.userData.vrmAnimations as VRMAnimation[] | undefined
      const first = animations?.[0]
      if (!first) {
        error.value = `No VRMA clip found in ${url}`
        return
      }

      const clip = createVRMAnimationClip(first, vrm)
      const action = mixer.value.clipAction(clip)
      clips.set(name, action)
    }
    catch (e) {
      error.value = e instanceof Error ? e.message : `Failed to load ${url}`
    }
  }

  /**
   * Fade-in the named clip, crossfading from the current one if any.
   * `duration` is the crossfade duration in seconds (default 0.3).
   */
  function play(name: string, duration = 0.3): void {
    const target = clips.get(name)
    if (!target) {
      error.value = `useVRMAnimation.play: unknown clip "${name}"`
      return
    }

    if (currentAction.value && currentAction.value !== target) {
      target.reset()
      target.crossFadeFrom(currentAction.value, duration, false)
    }
    target.play()
    currentAction.value = target
  }

  /**
   * Return the duration in seconds of a cached clip, or `null` if the
   * clip is unknown or its underlying AnimationClip is unavailable.
   *
   * Caller (Phase 3 hardening): compute a "return-to-idle" timer
   * aligned with the actual clip duration rather than a fixed
   * `emotionHoldMs` cap, so long wave/stretch animations don't get
   * chopped mid-motion.
   */
  function getClipDuration(name: string): number | null {
    const action = clips.get(name)
    if (!action) return null
    const duration = action.getClip()?.duration
    return typeof duration === 'number' && Number.isFinite(duration) && duration > 0
      ? duration
      : null
  }

  /** True when a clip with this name is already cached + ready to play. */
  function hasClip(name: string): boolean {
    return clips.has(name)
  }

  /**
   * Halts all actions — mouth stays at rest and the body stops moving.
   * Used when the avatar is interrupted.
   */
  function stop(): void {
    if (mixer.value) {
      mixer.value.stopAllAction()
    }
    currentAction.value = null
  }

  /**
   * Drive the underlying mixer forward by `delta` seconds. Call this
   * every rAF frame from the render loop, or the animations never
   * actually advance.
   */
  function update(delta: number): void {
    mixer.value?.update(delta)
  }

  function dispose(): void {
    stop()
    clips.clear()
    mixer.value = null
  }

  return {
    mixer,
    currentAction,
    error,
    attach,
    loadClip,
    hasClip,
    getClipDuration,
    play,
    stop,
    update,
    dispose,
  }
}
