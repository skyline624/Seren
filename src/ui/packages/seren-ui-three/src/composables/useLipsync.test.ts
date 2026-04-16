import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { VRM } from '@pixiv/three-vrm'
import { applyViseme, useLipsync, type VisemeTrackFrame } from './useLipsync'

function createFakeVrm(): { vrm: VRM, setValue: ReturnType<typeof vi.fn> } {
  const setValue = vi.fn()
  const vrm = {
    expressionManager: {
      setValue,
    },
  } as unknown as VRM
  return { vrm, setValue }
}

describe('applyViseme', () => {
  it('Apply_WithCanonicalViseme_ShouldResetMouthAndSetTarget', () => {
    // arrange
    const { vrm, setValue } = createFakeVrm()

    // act
    applyViseme(vrm, 'Aa', 0.8)

    // assert: 5 resets + 1 setValue for the target = 6 calls
    expect(setValue).toHaveBeenCalledTimes(6)
    // The last call is the target weight (0.8, not 0)
    const lastCall = setValue.mock.calls.at(-1)
    expect(lastCall?.[1]).toBe(0.8)
  })

  it('Apply_WithSilenceViseme_ShouldJustResetMouth', () => {
    // arrange
    const { vrm, setValue } = createFakeVrm()

    // act
    applyViseme(vrm, '-', 0)

    // assert
    expect(setValue).toHaveBeenCalledTimes(5)
    // Every call should be setting a mouth preset to 0.
    for (const call of setValue.mock.calls) {
      expect(call[1]).toBe(0)
    }
  })

  it('Apply_WithUnknownViseme_ShouldFallBackToAa', () => {
    // arrange
    const { vrm, setValue } = createFakeVrm()

    // act
    applyViseme(vrm, 'unknown-viseme', 0.5)

    // assert: last call should target the "aa" preset (lowercase "aa" in VRMExpressionPresetName).
    const lastCall = setValue.mock.calls.at(-1)
    expect(lastCall?.[0]).toBe('aa')
    expect(lastCall?.[1]).toBe(0.5)
  })

  it('Apply_WithWeightAboveOne_ShouldClampToOne', () => {
    // arrange
    const { vrm, setValue } = createFakeVrm()

    // act
    applyViseme(vrm, 'Aa', 1.5)

    // assert: the last call is the target, and it must be clamped to 1
    const lastCall = setValue.mock.calls.at(-1)
    expect(lastCall?.[0]).toBe('aa')
    expect(lastCall?.[1]).toBe(1)
  })

  it('Apply_WithNegativeWeight_ShouldClampToZero', () => {
    // arrange
    const { vrm, setValue } = createFakeVrm()

    // act
    applyViseme(vrm, 'Aa', -0.3)

    // assert
    const lastCall = setValue.mock.calls.at(-1)
    expect(lastCall?.[0]).toBe('aa')
    expect(lastCall?.[1]).toBe(0)
  })
})

describe('useLipsync', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('PlayTrack_WithTwoFrames_ShouldApplyEachAtItsStartTime', () => {
    // arrange
    const { vrm, setValue } = createFakeVrm()
    const lipsync = useLipsync(() => vrm)
    const track: VisemeTrackFrame[] = [
      { viseme: 'Aa', startTime: 0.1, duration: 0.05, weight: 1 },
      { viseme: 'Ih', startTime: 0.2, duration: 0.05, weight: 0.7 },
    ]

    // act
    lipsync.playTrack(track)
    expect(lipsync.isActive.value).toBe(true)
    expect(setValue).not.toHaveBeenCalled()

    vi.advanceTimersByTime(100)
    // First frame fired; target is "aa" with weight 1
    const aaCalls = setValue.mock.calls.filter(c => c[0] === 'aa' && c[1] === 1)
    expect(aaCalls.length).toBeGreaterThan(0)

    vi.advanceTimersByTime(100)
    const ihCalls = setValue.mock.calls.filter(c => c[0] === 'ih' && c[1] === 0.7)
    expect(ihCalls.length).toBeGreaterThan(0)

    // Last reset pass should fire at startTime+duration of the last frame.
    vi.advanceTimersByTime(60)
    expect(lipsync.isActive.value).toBe(false)
  })

  it('Stop_WhilePlaying_ShouldCancelPendingTimeoutsAndReset', () => {
    // arrange
    const { vrm, setValue } = createFakeVrm()
    const lipsync = useLipsync(() => vrm)
    lipsync.playTrack([
      { viseme: 'Aa', startTime: 1.0, duration: 0.1, weight: 1 },
    ])

    // act
    lipsync.stop()
    vi.advanceTimersByTime(2000)

    // assert: no viseme fired because stop ran before the timeout; only
    // the mouth reset from stop() should appear (5 resets, all weight 0).
    expect(lipsync.isActive.value).toBe(false)
    const nonZero = setValue.mock.calls.filter(c => c[1] !== 0)
    expect(nonZero).toHaveLength(0)
  })

  it('PlayTrack_WithEmptyTrack_ShouldBeNoOp', () => {
    // arrange
    const { vrm, setValue } = createFakeVrm()
    const lipsync = useLipsync(() => vrm)

    // act
    lipsync.playTrack([])
    vi.advanceTimersByTime(500)

    // assert
    expect(lipsync.isActive.value).toBe(false)
    expect(setValue).not.toHaveBeenCalled()
  })
})
