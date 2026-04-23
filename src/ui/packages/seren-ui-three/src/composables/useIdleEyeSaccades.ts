import type { VRM } from '@pixiv/three-vrm'
import { Object3D, Vector3 } from 'three'

/**
 * Idle eye-saccade driver for a VRM avatar. Every 0.4-2.5 s, the
 * gaze fixation point is jittered by ±0.25 world units on X and Y
 * around the caller's lookAt target. Produces the micro-movements
 * that make eyes feel alive ("saccades" in anatomy).
 *
 * Design (SRP + DIP + canonical three-vrm) :
 *  - Single-purpose: update one Object3D's position, which is the
 *    VRM's `lookAt.target`. The VRM system resolves gaze itself via
 *    `vrm.update(delta)` — we do NOT call `vrm.lookAt.update` manually
 *    (three-vrm official docs :
 *    https://pixiv.github.io/three-vrm/ — `vrm.update` handles lookAt).
 *  - Plays nicely with {@link useVRMLookAt} : when that composable
 *    is in `camera` or `pointer` mode, it owns the target and this
 *    saccade is a no-op (check via the passed `activeRef` option).
 *    In `off` mode, saccade takes over.
 *  - Deps injected via options : `random` + interval bounds.
 *
 * Canonical source : pattern ported from AIRI's `useIdleEyeSaccades`
 * (`stage-ui-three/animation.ts`) with the one simplification that
 * `vrm.update()` does the gaze resolution — matching pixiv docs.
 */

export const SACCADE_MIN_INTERVAL = 0.4
export const SACCADE_MAX_INTERVAL = 2.5
export const SACCADE_JITTER = 0.25
const DEFAULT_ANCHOR = new Vector3(0, 1.5, 1)

export interface SaccadeOptions {
  minInterval?: number
  maxInterval?: number
  jitter?: number
  random?: () => number
  /**
   * Reactive flag : when `false`, the composable stays a no-op —
   * used to step aside when `useVRMLookAt` is driving the gaze in
   * `camera` or `pointer` mode. Default `true` (saccade active).
   */
  activeRef?: () => boolean
  /**
   * Reactive gain on saccade *frequency*. 1 = baseline interval range.
   * 1.4 = more fidgety (shorter intervals), used when the avatar is
   * in `thinking` phase. 0 effectively freezes the gaze on the
   * current fixation.
   */
  gainRef?: () => number
}

export interface SaccadeController {
  update: (vrm: VRM | null | undefined, anchor: Vector3, delta: number) => void
  /** Expose the internal Object3D so the caller can inspect or attach it. */
  readonly target: Object3D
}

/**
 * Build a saccade controller. The `anchor` passed to `update` is the
 * user's "home" gaze (usually camera position or a scene point).
 * Saccades jitter the VRM's lookAt target around that anchor.
 */
export function useIdleEyeSaccades(opts: SaccadeOptions = {}): SaccadeController {
  const minInterval = opts.minInterval ?? SACCADE_MIN_INTERVAL
  const maxInterval = opts.maxInterval ?? SACCADE_MAX_INTERVAL
  const jitter = opts.jitter ?? SACCADE_JITTER
  const random = opts.random ?? Math.random
  const isActive = opts.activeRef ?? (() => true)
  const gain = opts.gainRef ?? (() => 1)

  // Object3D we own + substitute as vrm.lookAt.target when active.
  // Preallocated : zero GC pressure in the per-frame update.
  const target = new Object3D()
  const fixationTarget = new Vector3(DEFAULT_ANCHOR.x, DEFAULT_ANCHOR.y, DEFAULT_ANCHOR.z)
  let timeSinceLastSaccade = 0
  let nextSaccadeAfter = rollInterval()
  let hasAttached = false

  function rollInterval(): number {
    const g = Math.max(0.0001, gain())
    const raw = minInterval + random() * (maxInterval - minInterval)
    return raw / g
  }

  function rerollFixation(anchor: Vector3): void {
    fixationTarget.set(
      anchor.x + (random() * 2 - 1) * jitter,
      anchor.y + (random() * 2 - 1) * jitter,
      anchor.z,
    )
  }

  function update(vrm: VRM | null | undefined, anchor: Vector3, delta: number): void {
    if (!vrm || !vrm.lookAt) return
    if (!isActive()) return

    // Install our target on the VRM lookAt the first time we're active.
    // `autoUpdate = true` (default) + this target is enough — `vrm.update`
    // reads target.getWorldPosition() each frame.
    if (!hasAttached || vrm.lookAt.target !== target) {
      vrm.lookAt.target = target
      hasAttached = true
    }

    timeSinceLastSaccade += delta
    if (timeSinceLastSaccade >= nextSaccadeAfter) {
      rerollFixation(anchor)
      timeSinceLastSaccade = 0
      nextSaccadeAfter = rollInterval()
    }

    // Instant snap on each jump (saccades are ballistic by nature —
    // the real eye physics complete in < 50 ms; a 60 Hz lerp blurs
    // them unnaturally). AIRI uses `lerp(…, 1)` for the same reason.
    target.position.copy(fixationTarget)
  }

  return { update, target }
}
