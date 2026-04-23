import { describe, expect, it, vi } from 'vitest'
import type { VRM, VRMHumanBoneName } from '@pixiv/three-vrm'
import { Quaternion } from 'three'
import {
  BREATH_AMPLITUDE_DEFAULT,
  BREATH_PERIOD_DEFAULT,
  HIP_AMPLITUDE_DEFAULT,
  HIP_PERIOD_DEFAULT,
  useIdleBodySway,
  WEIGHT_AMPLITUDE_DEFAULT,
  WEIGHT_PERIOD_DEFAULT,
} from './useIdleBodySway'

/**
 * Build a fake VRM whose humanoid exposes the requested bones. Each
 * bone's `quaternion.multiply` is wrapped : it records a *clone* of
 * each incoming argument so downstream mutation (the composable
 * reuses its `tmpQuat` scratch) doesn't poison the assertion.
 */
function createFakeVrm(bones: VRMHumanBoneName[] = ['spine', 'chest', 'hips']): {
  vrm: VRM
  clones: Record<string, Quaternion[]>
  spies: Record<string, ReturnType<typeof vi.fn>>
  quaternions: Record<string, Quaternion>
} {
  const clones: Record<string, Quaternion[]> = {}
  const spies: Record<string, ReturnType<typeof vi.fn>> = {}
  const quaternions: Record<string, Quaternion> = {}

  const nodes: Record<string, { quaternion: Quaternion }> = {}
  for (const name of bones) {
    const q = new Quaternion()
    const perBoneClones: Quaternion[] = []
    const originalMultiply = q.multiply.bind(q)
    const spy = vi.fn((other: Quaternion) => {
      perBoneClones.push(other.clone())
      return originalMultiply(other)
    })
    ;(q as unknown as { multiply: typeof spy }).multiply = spy
    clones[name] = perBoneClones
    spies[name] = spy
    quaternions[name] = q
    nodes[name] = { quaternion: q }
  }

  const vrm = {
    humanoid: {
      getNormalizedBoneNode: (name: string) => nodes[name] ?? null,
    },
  } as unknown as VRM
  return { vrm, clones, spies, quaternions }
}

describe('useIdleBodySway', () => {
  it('Update_AfterDelta_ComposesRotationsOnAllThreeBones', () => {
    // arrange
    const { vrm, spies, clones } = createFakeVrm()
    const sway = useIdleBodySway()

    // act : at t = BREATH_PERIOD/4, sin(π/2) = 1 → peak rotation.
    sway.update(vrm, BREATH_PERIOD_DEFAULT / 4)

    // assert : each bone's multiply was called once with a non-identity quaternion.
    expect(spies.spine).toHaveBeenCalledTimes(1)
    expect(spies.chest).toHaveBeenCalledTimes(1)
    expect(spies.hips).toHaveBeenCalledTimes(1)

    // spine at peak ≈ sin(BREATH_AMPLITUDE / 2) on the x axis.
    const spineQuatClone = clones.spine![0]!
    expect(Math.abs(spineQuatClone.x)).toBeGreaterThan(0)
    expect(Math.abs(spineQuatClone.x)).toBeLessThan(0.015)  // << sin(0.02/2) * 2 upper bound
  })

  it('Update_EnabledFalse_IsNoOp', () => {
    const { vrm, spies } = createFakeVrm()
    const sway = useIdleBodySway({ enabledRef: () => false })

    sway.update(vrm, 1.0)

    expect(spies.spine).not.toHaveBeenCalled()
    expect(spies.chest).not.toHaveBeenCalled()
    expect(spies.hips).not.toHaveBeenCalled()
  })

  it('Update_GainZero_IsNoOp', () => {
    const { vrm, spies } = createFakeVrm()
    const sway = useIdleBodySway({ gainRef: () => 0 })

    sway.update(vrm, 1.0)

    expect(spies.spine).not.toHaveBeenCalled()
  })

  it('Update_GainDouble_ScalesAmplitudeLinearly', () => {
    // Run the same delta under gain=1 and gain=2 ; the resulting
    // quaternion x on spine should scale ≈ linearly because the
    // rotations are small (sin(2θ) ≈ 2·sin(θ) for small θ).
    const { vrm: vrm1, clones: c1 } = createFakeVrm()
    const s1 = useIdleBodySway()
    s1.update(vrm1, BREATH_PERIOD_DEFAULT / 4)
    const q1 = c1.spine![0]!

    const { vrm: vrm2, clones: c2 } = createFakeVrm()
    const s2 = useIdleBodySway({ gainRef: () => 2 })
    s2.update(vrm2, BREATH_PERIOD_DEFAULT / 4)
    const q2 = c2.spine![0]!

    const ratio = q2.x / q1.x
    expect(ratio).toBeCloseTo(2.0, 1)
  })

  it('Update_FrameRateIndependence_SmallVsBigDelta', () => {
    // Run the same total time as 2×(δ=Δ/2) vs 1×(δ=Δ). Trajectories
    // at the end must be ≤ 1e-6 rad apart — both hit the same t value
    // on the sinusoids, so the output at that point is identical.
    const { vrm: vrmA, clones: cA } = createFakeVrm()
    const swayA = useIdleBodySway()
    const DELTA = BREATH_PERIOD_DEFAULT / 8
    swayA.update(vrmA, DELTA / 2)
    swayA.update(vrmA, DELTA / 2)
    const qA = cA.spine!.at(-1)!

    const { vrm: vrmB, clones: cB } = createFakeVrm()
    const swayB = useIdleBodySway()
    swayB.update(vrmB, DELTA)
    const qB = cB.spine![0]!

    expect(Math.abs(qA.x - qB.x)).toBeLessThan(1e-6)
  })

  it('Update_MissingBones_StillProcessesAvailableOnes', () => {
    // Only 'spine' is available — chest + hips missing entirely.
    const { vrm, spies } = createFakeVrm(['spine'])
    const sway = useIdleBodySway()

    expect(() => sway.update(vrm, BREATH_PERIOD_DEFAULT / 4)).not.toThrow()
    expect(spies.spine).toHaveBeenCalledTimes(1)
  })

  it('Update_WithNullVrm_IsNoOp', () => {
    const sway = useIdleBodySway()
    expect(() => sway.update(null, 1.0)).not.toThrow()
    expect(() => sway.update(undefined, 1.0)).not.toThrow()
  })

  it('DefaultConstants_MatchIndustryNumbers', () => {
    expect(BREATH_PERIOD_DEFAULT).toBe(4.0)
    expect(BREATH_AMPLITUDE_DEFAULT).toBe(0.02)
    expect(WEIGHT_PERIOD_DEFAULT).toBe(6.0)
    expect(WEIGHT_AMPLITUDE_DEFAULT).toBe(0.015)
    expect(HIP_PERIOD_DEFAULT).toBe(8.0)
    expect(HIP_AMPLITUDE_DEFAULT).toBe(0.010)
  })
})
