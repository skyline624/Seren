import { describe, expect, it, vi } from 'vitest'
import type { VRM } from '@pixiv/three-vrm'
import {
  BLINK_DURATION,
  BLINK_EXPRESSION_NAME,
  BLINK_MAX_INTERVAL,
  BLINK_MIN_INTERVAL,
  useBlink,
} from './useBlink'

function createFakeVrm(): { vrm: VRM, setValue: ReturnType<typeof vi.fn> } {
  const setValue = vi.fn()
  const vrm = {
    expressionManager: {
      setValue,
    },
  } as unknown as VRM
  return { vrm, setValue }
}

describe('useBlink', () => {
  it('Update_BeforeFirstInterval_DoesNotBlink', () => {
    // arrange: random returns 0 → interval = minInterval (1s)
    const { vrm, setValue } = createFakeVrm()
    const blink = useBlink({ random: () => 0 })

    // act: 0.5 s elapsed — not yet at 1 s
    blink.update(vrm, 0.5)

    // assert
    expect(setValue).not.toHaveBeenCalled()
  })

  it('Update_CrossesInterval_TriggersBlink_WithSineCurve', () => {
    // arrange: random=0 → interval=1s, duration 0.2s.
    const { vrm, setValue } = createFakeVrm()
    const blink = useBlink({ random: () => 0 })

    // act: push time past the interval and into the blink window.
    blink.update(vrm, 1.0)  // accumulates 1s, triggers
    blink.update(vrm, 0.1)  // +0.1s → progress 0.5 → sin(π·0.5)=1

    // assert: second call wrote a value near 1.0 (peak)
    const peakCall = setValue.mock.calls.find(
      ([name, value]) => name === BLINK_EXPRESSION_NAME && Math.abs(value - 1) < 1e-9,
    )
    expect(peakCall).toBeDefined()
  })

  it('Update_OnBlinkCompletion_ResetsValueToZero', () => {
    // arrange
    const { vrm, setValue } = createFakeVrm()
    const blink = useBlink({ random: () => 0 })

    // act: trigger and fully complete the blink
    blink.update(vrm, 1.0)
    blink.update(vrm, 0.2)  // progress 1.0, closes

    // assert: last call on BLINK_EXPRESSION_NAME is 0
    const blinkWrites = setValue.mock.calls.filter(
      ([name]) => name === BLINK_EXPRESSION_NAME,
    )
    expect(blinkWrites.at(-1)?.[1]).toBe(0)
  })

  it('Update_AfterBlink_RollsNewInterval', () => {
    // arrange: first interval random=0 (min), second random=1 (near max)
    let callNo = 0
    const randomSeq = [0, 0.9999999]
    const { vrm, setValue } = createFakeVrm()
    const blink = useBlink({ random: () => randomSeq[callNo++] ?? 0 })

    // act: complete first blink, then try to trigger second at min interval
    blink.update(vrm, 1.0)
    blink.update(vrm, 0.2)  // completes, rolls next ≈ maxInterval
    setValue.mockClear()

    // Wait shorter than second interval — no blink expected
    blink.update(vrm, BLINK_MIN_INTERVAL + 0.01)
    expect(setValue).not.toHaveBeenCalled()

    // Wait past second interval — now blinks
    blink.update(vrm, BLINK_MAX_INTERVAL)
    expect(setValue).toHaveBeenCalled()
  })

  it('Update_WithNullVrm_IsNoOp', () => {
    const blink = useBlink({ random: () => 0 })
    expect(() => blink.update(null, 1.0)).not.toThrow()
    expect(() => blink.update(undefined, 1.0)).not.toThrow()
  })

  it('Update_WithCustomIntervals_HonorsOptions', () => {
    const { vrm, setValue } = createFakeVrm()
    const blink = useBlink({
      minInterval: 0.1,
      maxInterval: 0.1,
      duration: 0.05,
      random: () => 0,
    })

    // 0.1 s interval, random=0 → fires immediately past 0.1s
    blink.update(vrm, 0.11)
    expect(setValue).toHaveBeenCalled()
  })

  it('DefaultConstants_MatchDocumentedValues', () => {
    // Regression guard : intervals + duration are the documented
    // face-layer constants. Changing them is a behavior change.
    expect(BLINK_MIN_INTERVAL).toBe(1)
    expect(BLINK_MAX_INTERVAL).toBe(6)
    expect(BLINK_DURATION).toBe(0.2)
    expect(BLINK_EXPRESSION_NAME).toBe('blink')
  })
})
