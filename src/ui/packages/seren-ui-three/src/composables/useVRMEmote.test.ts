import { describe, expect, it, vi } from 'vitest'
import type { VRM } from '@pixiv/three-vrm'
import {
  DEFAULT_EMOTIONS,
  EMOTION_ALIASES,
  resolveEmotionKey,
  useVRMEmote,
} from './useVRMEmote'

function createFakeVrm(): { vrm: VRM, setValue: ReturnType<typeof vi.fn> } {
  const setValue = vi.fn()
  const vrm = {
    expressionManager: { setValue },
  } as unknown as VRM
  return { vrm, setValue }
}

class FakeClock {
  private nextId = 1
  readonly queue = new Map<number, { delay: number, fn: () => void }>()

  readonly setTimer = (fn: () => void, ms: number): number => {
    const id = this.nextId++
    this.queue.set(id, { delay: ms, fn })
    return id
  }

  readonly clearTimer = (id: number): void => {
    this.queue.delete(id)
  }

  flushNext(): number | null {
    const entry = [...this.queue.entries()][0]
    if (!entry) return null
    const [id, { delay, fn }] = entry
    this.queue.delete(id)
    fn()
    return delay
  }

  pendingCount(): number {
    return this.queue.size
  }
}

describe('resolveEmotionKey', () => {
  it.each([
    ['joy', 'joy'],
    ['happy', 'joy'],
    ['happiness', 'joy'],
    ['anger', 'anger'],
    ['angry', 'anger'],
    ['mad', 'anger'],
    ['sad', 'sad'],
    ['sadness', 'sad'],
    ['surprise', 'surprise'],
    ['surprised', 'surprise'],
    ['shocked', 'surprise'],
    ['relaxed', 'relaxed'],
    ['calm', 'relaxed'],
    ['neutral', 'neutral'],
    ['none', 'neutral'],
  ])('Resolve_Alias_%s_Returns_%s', (alias, expected) => {
    expect(resolveEmotionKey(alias)).toBe(expected)
  })

  it('Resolve_IsCaseInsensitive', () => {
    expect(resolveEmotionKey('HAPPY')).toBe('joy')
    expect(resolveEmotionKey('Anger')).toBe('anger')
  })

  it('Resolve_UnknownKey_ReturnsNull', () => {
    expect(resolveEmotionKey('thrilled')).toBeNull()
    expect(resolveEmotionKey('')).toBeNull()
    expect(resolveEmotionKey(null)).toBeNull()
    expect(resolveEmotionKey(undefined)).toBeNull()
  })
})

describe('useVRMEmote', () => {
  it('SetEmotion_Joy_BlendsToHappyExpression', () => {
    const { vrm, setValue } = createFakeVrm()
    const emote = useVRMEmote(() => vrm)

    emote.setEmotion('joy')
    // joy.blendDuration = 0.3 — advance to completion.
    emote.update(0.3)

    const happyCalls = setValue.mock.calls.filter(([name]) => name === 'happy')
    expect(happyCalls.length).toBeGreaterThan(0)
    // Last value should match target 0.7 at t=1.
    expect(happyCalls.at(-1)?.[1]).toBeCloseTo(0.7, 5)
  })

  it('SetEmotion_WithIntensity_Scales_PeakValue', () => {
    const { vrm, setValue } = createFakeVrm()
    const emote = useVRMEmote(() => vrm)

    emote.setEmotion('joy', 0.5)
    emote.update(0.3)  // completes

    const happyCalls = setValue.mock.calls.filter(([name]) => name === 'happy')
    expect(happyCalls.at(-1)?.[1]).toBeCloseTo(0.7 * 0.5, 5)
  })

  it('SetEmotion_Unknown_FallsBackToNeutral_AndEmitsDebug', () => {
    const { vrm, setValue } = createFakeVrm()
    const onDebug = vi.fn()
    const emote = useVRMEmote(() => vrm, { onDebug })

    emote.setEmotion('thrilled')
    emote.update(0.3)

    expect(onDebug).toHaveBeenCalledWith('unknown', expect.objectContaining({
      requested: 'thrilled',
      fallback: 'neutral',
    }))
    // Writes to `neutral` preset.
    const neutralCalls = setValue.mock.calls.filter(([name]) => name === 'neutral')
    expect(neutralCalls.length).toBeGreaterThan(0)
  })

  it('Update_MidBlend_WritesEasedInterpolatedValue', () => {
    const { vrm, setValue } = createFakeVrm()
    const emote = useVRMEmote(() => vrm)

    emote.setEmotion('joy')
    emote.update(0.15)  // t = 0.5 of 0.3s → easeInOutCubic(0.5) = 0.5

    const lastHappy = setValue.mock.calls
      .filter(([name]) => name === 'happy')
      .at(-1)
    expect(lastHappy).toBeDefined()
    // 0 + (0.7 - 0) * 0.5 = 0.35
    expect(lastHappy?.[1]).toBeCloseTo(0.35, 5)
  })

  it('SetEmotionWithResetAfter_SchedulesReset_ViaInjectedTimer', () => {
    const { vrm, setValue } = createFakeVrm()
    const clock = new FakeClock()
    const emote = useVRMEmote(() => vrm, {
      setTimer: clock.setTimer,
      clearTimer: clock.clearTimer,
    })

    emote.setEmotionWithResetAfter('joy', 1000)
    expect(clock.pendingCount()).toBe(1)

    // Flush the scheduled reset — should trigger a blend to neutral.
    clock.flushNext()
    setValue.mockClear()
    emote.update(0.3)  // neutral.blendDuration = 0.3

    // Writes to 'neutral' preset.
    expect(setValue.mock.calls.some(([name]) => name === 'neutral')).toBe(true)
  })

  it('SetEmotion_Sequential_RerollsTimer', () => {
    const { vrm } = createFakeVrm()
    const clock = new FakeClock()
    const emote = useVRMEmote(() => vrm, {
      setTimer: clock.setTimer,
      clearTimer: clock.clearTimer,
    })

    emote.setEmotionWithResetAfter('joy', 1000)
    expect(clock.pendingCount()).toBe(1)

    // Switching emotion mid-reset should clear the previous timer.
    emote.setEmotionWithResetAfter('anger', 500)
    expect(clock.pendingCount()).toBe(1)  // old cleared, new scheduled
  })

  it('Update_PriorExpressionDecaysToZero_WhenNewEmotionLacksIt', () => {
    const { vrm, setValue } = createFakeVrm()
    const emote = useVRMEmote(() => vrm)

    emote.setEmotion('joy')        // writes 'happy'
    emote.update(0.3)              // completes at value=0.7

    setValue.mockClear()
    emote.setEmotion('anger')      // switches to 'angry'; 'happy' should decay
    emote.update(0.3)              // completes

    // Final values : happy → 0, angry → target
    const happyFinal = setValue.mock.calls
      .filter(([name]) => name === 'happy')
      .at(-1)
    expect(happyFinal?.[1]).toBeCloseTo(0, 5)
  })

  it('Dispose_ClearsTimer_AndIsIdempotent', () => {
    const { vrm } = createFakeVrm()
    const clock = new FakeClock()
    const emote = useVRMEmote(() => vrm, {
      setTimer: clock.setTimer,
      clearTimer: clock.clearTimer,
    })

    emote.setEmotionWithResetAfter('joy', 1000)
    emote.dispose()
    expect(clock.pendingCount()).toBe(0)

    expect(() => emote.dispose()).not.toThrow()
  })

  it('DefaultEmotionMap_HasExpectedKeys', () => {
    expect(Object.keys(DEFAULT_EMOTIONS).sort()).toEqual([
      'anger', 'joy', 'neutral', 'relaxed', 'sad', 'surprise',
    ])
    // Regression guard : aliases cover the 6 canonical keys.
    for (const canonical of Object.keys(DEFAULT_EMOTIONS)) {
      expect(Object.values(EMOTION_ALIASES)).toContain(canonical)
    }
  })
})
