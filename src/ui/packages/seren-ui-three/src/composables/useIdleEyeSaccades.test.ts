import { describe, expect, it } from 'vitest'
import type { VRM } from '@pixiv/three-vrm'
import { Object3D, Vector3 } from 'three'
import {
  SACCADE_JITTER,
  SACCADE_MAX_INTERVAL,
  SACCADE_MIN_INTERVAL,
  useIdleEyeSaccades,
} from './useIdleEyeSaccades'

function createFakeVrm(): { vrm: VRM, lookAt: { target: Object3D | null } } {
  const lookAt = { target: null as Object3D | null }
  const vrm = { lookAt } as unknown as VRM
  return { vrm, lookAt }
}

describe('useIdleEyeSaccades', () => {
  it('Update_AttachesTargetToVrmLookAt_OnFirstActive', () => {
    // arrange
    const { vrm, lookAt } = createFakeVrm()
    const saccade = useIdleEyeSaccades({ random: () => 0 })
    expect(lookAt.target).toBeNull()

    // act: one tick triggers attachment
    saccade.update(vrm, new Vector3(0, 1.5, 1), 0.1)

    // assert
    expect(lookAt.target).toBe(saccade.target)
  })

  it('Update_JittersWithinBounds_OfAnchor', () => {
    // arrange: random=1 → max positive jitter on both axes
    const { vrm } = createFakeVrm()
    const saccade = useIdleEyeSaccades({ random: () => 1, minInterval: 0, maxInterval: 0 })
    const anchor = new Vector3(0, 1.5, 1)

    // act: a non-zero delta triggers the first reroll
    saccade.update(vrm, anchor, 0.01)

    // assert: jitter is bounded — at random=1, (2·1 - 1)*jitter = +jitter.
    expect(saccade.target.position.x).toBeCloseTo(anchor.x + SACCADE_JITTER, 6)
    expect(saccade.target.position.y).toBeCloseTo(anchor.y + SACCADE_JITTER, 6)
    expect(saccade.target.position.z).toBeCloseTo(anchor.z, 6)
  })

  it('Update_JitterIsSymmetric_WithRandom_Zero', () => {
    // arrange: random=0 → (0 - 1)*jitter = -jitter
    const { vrm } = createFakeVrm()
    const saccade = useIdleEyeSaccades({ random: () => 0, minInterval: 0, maxInterval: 0 })
    const anchor = new Vector3(0, 1.5, 1)

    // act
    saccade.update(vrm, anchor, 0.01)

    // assert
    expect(saccade.target.position.x).toBeCloseTo(anchor.x - SACCADE_JITTER, 6)
    expect(saccade.target.position.y).toBeCloseTo(anchor.y - SACCADE_JITTER, 6)
  })

  it('Update_DoesNotRerollBeforeInterval', () => {
    // arrange: random=0.5 → interval = 1.45s
    const { vrm } = createFakeVrm()
    let random = 0.5
    const saccade = useIdleEyeSaccades({ random: () => random })
    const anchor = new Vector3(0, 1.5, 1)

    // act: two updates, both under the interval
    saccade.update(vrm, anchor, 0.1)
    const firstPos = saccade.target.position.clone()
    random = 0.99  // would dramatically change jitter if rerolled
    saccade.update(vrm, anchor, 0.3)  // total 0.4s — still < interval

    // assert: same position (reroll gated)
    expect(saccade.target.position.x).toBe(firstPos.x)
    expect(saccade.target.position.y).toBe(firstPos.y)
  })

  it('Update_InactiveRef_IsNoOp', () => {
    const { vrm, lookAt } = createFakeVrm()
    const saccade = useIdleEyeSaccades({
      random: () => 0,
      activeRef: () => false,
    })

    saccade.update(vrm, new Vector3(0, 1.5, 1), 0.1)

    // No attachment happened — lookAt.target still null.
    expect(lookAt.target).toBeNull()
  })

  it('Update_WithNullVrm_IsNoOp', () => {
    const saccade = useIdleEyeSaccades({ random: () => 0 })
    expect(() => saccade.update(null, new Vector3(0, 1.5, 1), 0.1)).not.toThrow()
    expect(() => saccade.update(undefined, new Vector3(0, 1.5, 1), 0.1)).not.toThrow()
  })

  it('DefaultConstants_MatchDocumentedRanges', () => {
    expect(SACCADE_MIN_INTERVAL).toBe(0.4)
    expect(SACCADE_MAX_INTERVAL).toBe(2.5)
    expect(SACCADE_JITTER).toBe(0.25)
  })
})
