import type { VRM } from '@pixiv/three-vrm'

/**
 * Auto-blinking face layer for a VRM avatar. Produces a natural
 * blink every 1-6 s with a smooth 0.2 s sine-eased animation.
 *
 * Design (SRP + DIP):
 *  - Stateless from the caller's perspective — the caller just ticks
 *    `update(vrm, delta)` each frame. Internal state (time since last
 *    blink, current progress) is scoped to the composable instance.
 *  - Deps injected via options : `random` + constants — fully
 *    testable without touching `Math.random` or global time.
 *  - Writes exclusively to `VRMExpressionManager.setValue('blink', …)`.
 *    No bone mutation — the face layer is independent of body layers
 *    (Warudo / VRChat Playable Layers convention).
 *
 * Canonical source : pattern ported from pixiv/three-vrm expression
 * manager docs + AIRI's `useBlink` in `stage-ui-three/animation.ts`.
 */

/** Min interval (seconds) between successive blinks. */
export const BLINK_MIN_INTERVAL = 1
/** Max interval (seconds) between successive blinks. */
export const BLINK_MAX_INTERVAL = 6
/** Blink duration (seconds) — a short sine rise + fall. */
export const BLINK_DURATION = 0.2
/** Name of the VRM expression preset driven by this composable. */
export const BLINK_EXPRESSION_NAME = 'blink'

export interface BlinkOptions {
  /** Override min interval (sec). Defaults to {@link BLINK_MIN_INTERVAL}. */
  minInterval?: number
  /** Override max interval (sec). Defaults to {@link BLINK_MAX_INTERVAL}. */
  maxInterval?: number
  /** Override duration (sec). Defaults to {@link BLINK_DURATION}. */
  duration?: number
  /** Uniform PRNG in [0, 1). Defaults to `Math.random` — override in tests. */
  random?: () => number
  /**
   * Reactive gain on blink *frequency*. 1 = baseline interval range.
   * 1.5 = 50% more blinks per minute (shorter intervals). 0 disables.
   * Driven by the avatar-state layer gains (Phase 5) so `thinking`
   * lowers the blink rate and `talking` raises it slightly.
   */
  gainRef?: () => number
}

export interface BlinkController {
  /**
   * Advance the blink state by `delta` seconds and write to the VRM
   * expression manager. No-op when `vrm` is null/undefined.
   */
  update: (vrm: VRM | null | undefined, delta: number) => void
}

/**
 * Build a blink controller. Call `update(vrm, delta)` once per frame,
 * AFTER the base animation mixer but BEFORE `vrm.update(delta)` — the
 * latter reads the expression manager state set here.
 */
export function useBlink(opts: BlinkOptions = {}): BlinkController {
  const minInterval = opts.minInterval ?? BLINK_MIN_INTERVAL
  const maxInterval = opts.maxInterval ?? BLINK_MAX_INTERVAL
  const duration = opts.duration ?? BLINK_DURATION
  const random = opts.random ?? Math.random
  const gain = opts.gainRef ?? (() => 1)

  let timeSinceLastBlink = 0
  let nextBlinkTime = rollInterval()
  let isBlinking = false
  let blinkProgress = 0

  function rollInterval(): number {
    // Frequency gain > 1 ⇒ shorter intervals (more blinks). Clamp
    // gain to avoid division by zero or negative numbers — a gain of
    // exactly 0 effectively disables blinking by pushing the next
    // interval past any reasonable runtime horizon.
    const g = Math.max(0.0001, gain())
    const raw = minInterval + random() * (maxInterval - minInterval)
    return raw / g
  }

  function update(vrm: VRM | null | undefined, delta: number): void {
    if (!vrm) return
    const manager = vrm.expressionManager
    if (!manager) return

    timeSinceLastBlink += delta

    if (!isBlinking) {
      if (timeSinceLastBlink < nextBlinkTime) return
      // Cross the interval. Use the overflow as the starting progress
      // so that, at extreme deltas (test harness, long pauses, browser
      // tab un-throttling), the blink doesn't "teleport" forward — the
      // sine curve picks up where real time says it should be.
      isBlinking = true
      const overflow = timeSinceLastBlink - nextBlinkTime
      blinkProgress = overflow / duration
    }
    else {
      blinkProgress += delta / duration
    }

    // Sine curve : 0 → 1 → 0 over the full duration. `sin(π·t)` peaks
    // at t=0.5 and returns to 0 at t=1, matching the natural eyelid
    // close-then-open shape.
    const value = Math.sin(Math.PI * Math.min(1, blinkProgress))
    manager.setValue(BLINK_EXPRESSION_NAME, value)

    if (blinkProgress >= 1) {
      isBlinking = false
      blinkProgress = 0
      timeSinceLastBlink = 0
      nextBlinkTime = rollInterval()
      manager.setValue(BLINK_EXPRESSION_NAME, 0)
    }
  }

  return { update }
}
