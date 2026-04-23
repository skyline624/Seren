import { describe, it, expect, vi } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import type { IdleAnimation } from './idleAnimationCatalog'
import { useIdleAnimationScheduler } from './useIdleAnimationScheduler'

/**
 * Deterministic timer stub that lets tests drive a fake clock without
 * touching `globalThis.setTimeout`. Avoids vitest fake-timer coupling
 * with Vue's reactivity system (which runs flushes in microtasks).
 */
class FakeClock {
  private nextId = 1
  private readonly queue = new Map<number, { delay: number, fn: () => void }>()

  readonly setTimer = (fn: () => void, ms: number): number => {
    const id = this.nextId++
    this.queue.set(id, { delay: ms, fn })
    return id
  }

  readonly clearTimer = (id: number): void => {
    this.queue.delete(id)
  }

  /** Fire the currently-pending timer and return its delay (for asserts). */
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

function buildCatalog(): IdleAnimation[] {
  return [
    { id: 'a', durationMs: 1000, moodWeights: { neutral: 1 } },
    { id: 'b', durationMs: 1000, moodWeights: { neutral: 0 } },
  ]
}

describe('useIdleAnimationScheduler', () => {
  it('fires onTrigger once per interval and re-arms afterwards', () => {
    const scope = effectScope()
    try {
      scope.run(() => {
        const clock = new FakeClock()
        const trigger = vi.fn()
        const isActive = ref(true)
        const mood = ref<string | null>(null)
        const intervalSeconds = ref<readonly [number, number]>([10, 20])
        const enabled = ref(true)

        useIdleAnimationScheduler({
          isActive,
          mood,
          intervalSeconds,
          enabled,
          onTrigger: trigger,
          catalog: buildCatalog(),
          random: () => 0, // always picks 'a', minimum interval
          setTimer: clock.setTimer as unknown as typeof setTimeout,
          clearTimer: clock.clearTimer as unknown as typeof clearTimeout,
        })

        expect(clock.pendingCount()).toBe(1)
        const firstDelay = clock.flushNext()
        expect(firstDelay).toBe(10 * 1000) // min interval, random=0
        expect(trigger).toHaveBeenCalledTimes(1)
        expect(trigger.mock.calls[0]?.[0]?.id).toBe('a')

        // Scheduler should have re-armed for the next fire.
        expect(clock.pendingCount()).toBe(1)
      })
    }
    finally {
      scope.stop()
    }
  })

  it('cancels the pending timer when isActive flips to false', async () => {
    const scope = effectScope()
    try {
      const clock = new FakeClock()
      const trigger = vi.fn()
      const isActive = ref(true)
      const enabled = ref(true)

      scope.run(() => {
        useIdleAnimationScheduler({
          isActive,
          mood: ref(null),
          intervalSeconds: ref<readonly [number, number]>([10, 20]),
          enabled,
          onTrigger: trigger,
          catalog: buildCatalog(),
          random: () => 0.5,
          setTimer: clock.setTimer as unknown as typeof setTimeout,
          clearTimer: clock.clearTimer as unknown as typeof clearTimeout,
        })
      })

      expect(clock.pendingCount()).toBe(1)
      isActive.value = false
      await nextTick()
      expect(clock.pendingCount()).toBe(0)
      expect(trigger).not.toHaveBeenCalled()
    }
    finally {
      scope.stop()
    }
  })

  it('never arms the timer when enabled is false', () => {
    const scope = effectScope()
    try {
      scope.run(() => {
        const clock = new FakeClock()
        useIdleAnimationScheduler({
          isActive: ref(true),
          mood: ref(null),
          intervalSeconds: ref<readonly [number, number]>([10, 20]),
          enabled: ref(false),
          onTrigger: vi.fn(),
          catalog: buildCatalog(),
          random: () => 0,
          setTimer: clock.setTimer as unknown as typeof setTimeout,
          clearTimer: clock.clearTimer as unknown as typeof clearTimeout,
        })
        expect(clock.pendingCount()).toBe(0)
      })
    }
    finally {
      scope.stop()
    }
  })

  it('re-arms when enabled flips from false to true', async () => {
    const scope = effectScope()
    try {
      const clock = new FakeClock()
      const enabled = ref(false)
      scope.run(() => {
        useIdleAnimationScheduler({
          isActive: ref(true),
          mood: ref(null),
          intervalSeconds: ref<readonly [number, number]>([10, 20]),
          enabled,
          onTrigger: vi.fn(),
          catalog: buildCatalog(),
          random: () => 0,
          setTimer: clock.setTimer as unknown as typeof setTimeout,
          clearTimer: clock.clearTimer as unknown as typeof clearTimeout,
        })
      })
      expect(clock.pendingCount()).toBe(0)
      enabled.value = true
      await nextTick()
      expect(clock.pendingCount()).toBe(1)
    }
    finally {
      scope.stop()
    }
  })

  it('respects the dynamic interval window after a change', async () => {
    const scope = effectScope()
    try {
      const clock = new FakeClock()
      const interval = ref<readonly [number, number]>([10, 10])
      scope.run(() => {
        useIdleAnimationScheduler({
          isActive: ref(true),
          mood: ref(null),
          intervalSeconds: interval,
          enabled: ref(true),
          onTrigger: vi.fn(),
          catalog: buildCatalog(),
          random: () => 0,
          setTimer: clock.setTimer as unknown as typeof setTimeout,
          clearTimer: clock.clearTimer as unknown as typeof clearTimeout,
        })
      })

      // Change the window — scheduler's watch re-arms with the new range.
      interval.value = [30, 30]
      await nextTick()
      const delay = clock.flushNext()
      expect(delay).toBe(30 * 1000)
    }
    finally {
      scope.stop()
    }
  })

  it('never arms the timer when the catalog is empty (no .vrma registered)', () => {
    const scope = effectScope()
    try {
      scope.run(() => {
        const clock = new FakeClock()
        const trigger = vi.fn()
        useIdleAnimationScheduler({
          isActive: ref(true),
          mood: ref(null),
          intervalSeconds: ref<readonly [number, number]>([10, 20]),
          enabled: ref(true),
          onTrigger: trigger,
          catalog: [],
          random: () => 0,
          setTimer: clock.setTimer as unknown as typeof setTimeout,
          clearTimer: clock.clearTimer as unknown as typeof clearTimeout,
        })

        // No clips registered → scheduler is enabled but silent.
        expect(clock.pendingCount()).toBe(0)
        expect(trigger).not.toHaveBeenCalled()
      })
    }
    finally {
      scope.stop()
    }
  })
})
