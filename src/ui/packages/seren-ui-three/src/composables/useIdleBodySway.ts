import type { VRM, VRMHumanBoneName } from '@pixiv/three-vrm'
import { Euler, Quaternion } from 'three'

/**
 * Procedural body-sway layer : three phase-shifted sinusoids drive
 * subtle rotations on spine (breath pitch), chest (weight-shift roll),
 * and hips (gait yaw). Runs AFTER the mixer — the rotations are
 * composed on top of the base idle clip via `quaternion.multiply`
 * rather than replacing it. Equivalent math to a Three.js
 * `AdditiveAnimationBlendMode` layer, without requiring a pre-baked
 * additive clip (see plan §2 for the rationale).
 *
 * Design (SRP + KISS + industry-backed constants) :
 *  - No external library, no Perlin noise. Three sinusoids at
 *    co-prime periods produce perceptibly non-repeating motion.
 *  - Amplitudes / periods are aligned with Animaze breathing
 *    (uniform scale 1 → 1.025, ~4 s cycle) and VRChat "Additive"
 *    layer guidelines (subtle, < 2°).
 *  - Preallocated scratch `Euler` + `Quaternion` → zero allocations
 *    inside the per-frame `update` hot path.
 *  - Graceful partial VRMs : missing bones silently skipped.
 */

export const BREATH_PERIOD_DEFAULT = 4.0     // seconds — natural resting rate
export const BREATH_AMPLITUDE_DEFAULT = 0.02 // rad (~1.1°) — Animaze-aligned
export const WEIGHT_PERIOD_DEFAULT = 6.0
export const WEIGHT_AMPLITUDE_DEFAULT = 0.015
export const HIP_PERIOD_DEFAULT = 8.0
export const HIP_AMPLITUDE_DEFAULT = 0.010

export interface BodySwayOptions {
  breathPeriod?: number
  breathAmplitude?: number
  weightPeriod?: number
  weightAmplitude?: number
  hipPeriod?: number
  hipAmplitude?: number
  /** Reactive enable flag. `false` → update is a no-op. Default `true`. */
  enabledRef?: () => boolean
  /**
   * Global multiplier applied to all three amplitudes each tick. Fed
   * by the avatar-state FSM (Phase 5) so `thinking` can dampen motion
   * and `talking` amplify it. Default `() => 1`.
   */
  gainRef?: () => number
}

export interface BodySwayController {
  update: (vrm: VRM | null | undefined, delta: number) => void
}

// Preallocated scratch — never re-instantiated inside update().
const tmpEuler = new Euler()
const tmpQuat = new Quaternion()

export function useIdleBodySway(opts: BodySwayOptions = {}): BodySwayController {
  const breathPeriod = opts.breathPeriod ?? BREATH_PERIOD_DEFAULT
  const breathAmp = opts.breathAmplitude ?? BREATH_AMPLITUDE_DEFAULT
  const weightPeriod = opts.weightPeriod ?? WEIGHT_PERIOD_DEFAULT
  const weightAmp = opts.weightAmplitude ?? WEIGHT_AMPLITUDE_DEFAULT
  const hipPeriod = opts.hipPeriod ?? HIP_PERIOD_DEFAULT
  const hipAmp = opts.hipAmplitude ?? HIP_AMPLITUDE_DEFAULT

  const isEnabled = opts.enabledRef ?? (() => true)
  const gain = opts.gainRef ?? (() => 1)

  // Phase shifts (in radians) keep the three sinusoids out of sync so
  // the overall motion doesn't look periodic. Derived from co-prime
  // fractions of π — arbitrary but stable.
  const SPINE_PHASE = 0
  const CHEST_PHASE = Math.PI / 3
  const HIPS_PHASE = Math.PI / 5

  let t = 0  // accumulated seconds

  function applyCompose(vrm: VRM, bone: VRMHumanBoneName, x: number, y: number, z: number): void {
    const node = vrm.humanoid?.getNormalizedBoneNode(bone)
    if (!node) return
    tmpEuler.set(x, y, z)
    tmpQuat.setFromEuler(tmpEuler)
    node.quaternion.multiply(tmpQuat)
  }

  function update(vrm: VRM | null | undefined, delta: number): void {
    if (!vrm) return
    if (!isEnabled()) return

    t += delta
    const g = Math.max(0, gain())
    if (g === 0) return

    // Breath : pitch the spine very slightly forward / back.
    const breath = Math.sin((2 * Math.PI * t) / breathPeriod + SPINE_PHASE)
      * breathAmp * g

    // Weight shift : roll the chest left / right at a longer period.
    const weightShift = Math.sin((2 * Math.PI * t) / weightPeriod + CHEST_PHASE)
      * weightAmp * g

    // Hip sway : yaw on the hips at an even longer period.
    const hipSway = Math.sin((2 * Math.PI * t) / hipPeriod + HIPS_PHASE)
      * hipAmp * g

    applyCompose(vrm, 'spine', breath, 0, 0)
    applyCompose(vrm, 'chest', 0, 0, weightShift)
    applyCompose(vrm, 'hips', 0, hipSway, 0)
  }

  return { update }
}
