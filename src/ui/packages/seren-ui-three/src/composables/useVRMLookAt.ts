import type { VRM } from '@pixiv/three-vrm'
import type { Camera } from 'three'
import { Object3D, Vector3 } from 'three'
import { onBeforeUnmount, watchEffect } from 'vue'

export type EyeTrackingMode = 'camera' | 'pointer' | 'off'

/**
 * Drives the VRM's `lookAt` target based on a user-selected mode:
 *
 * - `'camera'` — the model tracks the scene camera (classic AIRI
 *   default; eyes feel "present" to the viewer).
 * - `'pointer'` — the model follows the mouse pointer within the
 *   viewport. Implemented by mapping the NDC cursor position into
 *   world space at the camera's distance, so the gaze target moves
 *   on a virtual plane parallel to the camera.
 * - `'off'` — the lookAt target is cleared; the model's eyes stay
 *   neutral (bone animation / expression manager decide).
 *
 * Defensive against VRMs with no `lookAt` (some static / low-poly
 * models ship without eye bones): the composable no-ops in that case
 * instead of throwing every frame.
 *
 * The `target` passed to `vrm.lookAt` is a persistent `Object3D` we own
 * — updating its world position each pointer move is much cheaper than
 * reallocating a Vector3 target, and VRMLookAt happily reads off any
 * Object3D with a world transform.
 */
export function useVRMLookAt(
  vrmRef: () => VRM | null,
  modeRef: () => EyeTrackingMode,
  cameraRef: () => Camera | null,
  canvasRef: () => HTMLElement | null = () => null,
): { dispose: () => void } {
  const target = new Object3D()
  // We need a reusable vector to decode NDC → world without allocating.
  const ndc = new Vector3()

  let pointerListener: ((ev: PointerEvent) => void) | null = null
  let lastPointerRaf = 0

  function detachPointerListener(): void {
    const el = canvasRef() ?? window
    if (pointerListener) {
      ;(el as EventTarget).removeEventListener('pointermove', pointerListener as EventListener)
      pointerListener = null
    }
    if (lastPointerRaf !== 0) {
      cancelAnimationFrame(lastPointerRaf)
      lastPointerRaf = 0
    }
  }

  function attachPointerListener(): void {
    detachPointerListener()
    const el = canvasRef() ?? window
    let pendingX = 0
    let pendingY = 0
    let pending = false

    pointerListener = (ev: PointerEvent) => {
      const rect = (canvasRef()?.getBoundingClientRect())
        ?? { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight }
      pendingX = ((ev.clientX - rect.left) / rect.width) * 2 - 1
      pendingY = -(((ev.clientY - rect.top) / rect.height) * 2 - 1)

      // Coalesce multiple moves per frame — raycasting / world unproject
      // is cheap but still not free, and a modern mouse fires hundreds of
      // events per second.
      if (pending) return
      pending = true
      lastPointerRaf = requestAnimationFrame(() => {
        pending = false
        const camera = cameraRef()
        if (!camera) return
        // Unproject the NDC pointer at z=0.5 (mid-frustum) into world.
        // `target` is a plain Object3D, not attached to any scene graph,
        // so setting its position is enough — VRMLookAt reads world
        // position only.
        ndc.set(pendingX, pendingY, 0.5).unproject(camera)
        target.position.copy(ndc)
        target.updateMatrixWorld()
      })
    }
    ;(el as EventTarget).addEventListener('pointermove', pointerListener as EventListener)
  }

  // Reactive mode switch — runs whenever the mode or vrm changes.
  const stopWatch = watchEffect(() => {
    const vrm = vrmRef()
    if (!vrm?.lookAt) return

    const mode = modeRef()

    if (mode === 'off') {
      vrm.lookAt.target = null
      detachPointerListener()
      return
    }

    if (mode === 'camera') {
      detachPointerListener()
      const camera = cameraRef()
      // The camera itself is an Object3D — VRMLookAt can track it directly
      // and will always see its up-to-date world transform.
      vrm.lookAt.target = camera ?? null
      return
    }

    // 'pointer'
    vrm.lookAt.target = target
    attachPointerListener()
  })

  function dispose(): void {
    detachPointerListener()
    stopWatch()
    const vrm = vrmRef()
    if (vrm?.lookAt) vrm.lookAt.target = null
  }

  onBeforeUnmount(dispose)

  return { dispose }
}
