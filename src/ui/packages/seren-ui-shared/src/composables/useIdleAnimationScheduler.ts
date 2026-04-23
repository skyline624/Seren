import { onScopeDispose, watch } from 'vue'
import type { Ref } from 'vue'
import type { IdleAnimation } from './idleAnimationCatalog'
import { DEFAULT_IDLE_CATALOG, pickNextIdle } from './idleAnimationCatalog'

/**
 * Options for the idle animation scheduler.
 *
 * All refs are reactive inputs the scheduler watches. The `onTrigger`
 * callback is the output: the scheduler never mutates the chat store
 * directly — it delegates the "how do we surface this?" decision to the
 * caller (SRP + DIP). `AvatarStage.vue` currently adapts it into a
 * `currentAction` emission, but a unit test can just record the calls.
 */
export interface IdleAnimationSchedulerOptions {
  /** Reactive flag: `true` when the avatar is free to idle (not
   *  streaming, not thinking, not listening). Scheduler arms when
   *  `true`, cancels any pending timer when `false`. */
  isActive: Ref<boolean>

  /** Reactive mood id (e.g. `"neutral"`, `"joy"`, `"sad"`). Fed to
   *  `pickNextIdle` for weighted selection. `null` → neutral defaults. */
  mood: Ref<string | null>

  /** Reactive `[minSeconds, maxSeconds]` interval window. The scheduler
   *  picks a uniform random delay inside the window between fires, so
   *  nothing feels mechanical. */
  intervalSeconds: Ref<readonly [number, number]>

  /** Reactive feature-flag. `false` → scheduler never arms. */
  enabled: Ref<boolean>

  /** Invoked each time the scheduler decides to play an animation. */
  onTrigger: (animation: IdleAnimation) => void

  /** Optional catalog override (defaults to `DEFAULT_IDLE_CATALOG`). */
  catalog?: readonly IdleAnimation[]

  /** Optional PRNG for deterministic tests. Defaults to `Math.random`. */
  random?: () => number

  /** Optional timer handles — overridden in tests to use fake timers
   *  without touching `globalThis`. */
  setTimer?: (fn: () => void, ms: number) => ReturnType<typeof setTimeout>
  clearTimer?: (handle: ReturnType<typeof setTimeout>) => void
}

/**
 * Vue composable: a jittered state machine that fires idle animations
 * during pauses. Lives inside a component (or any other Vue effect
 * scope) so the watchers + timer auto-clean up on unmount.
 *
 * Design intent (KISS): a single `setTimeout` chain, no event loop
 * library. Watchers re-arm the chain when the inputs change. The only
 * output is the `onTrigger` callback — callers route it into their
 * own systems (for us, `chatStore.currentAction`).
 */
export function useIdleAnimationScheduler(opts: IdleAnimationSchedulerOptions): {
  /** Cancel the pending timer and detach the scheduler. Idempotent. */
  stop: () => void
} {
  const catalog = opts.catalog ?? DEFAULT_IDLE_CATALOG
  const random = opts.random ?? Math.random
  const setTimer = opts.setTimer ?? ((fn, ms) => setTimeout(fn, ms))
  const clearTimer = opts.clearTimer ?? ((handle) => { clearTimeout(handle) })

  let pending: ReturnType<typeof setTimeout> | null = null

  function cancel(): void {
    if (pending !== null) {
      clearTimer(pending)
      pending = null
    }
  }

  function schedule(): void {
    cancel()

    if (!opts.enabled.value || !opts.isActive.value) {
      return
    }

    const [minSec, maxSec] = opts.intervalSeconds.value
    const lo = Math.max(1, minSec)
    const hi = Math.max(lo, maxSec)
    const delayMs = (lo + random() * (hi - lo)) * 1000

    pending = setTimer(() => {
      pending = null
      // Re-check conditions in case they changed during the wait.
      if (!opts.enabled.value || !opts.isActive.value) {
        return
      }
      const next = pickNextIdle(catalog, opts.mood.value, random)
      opts.onTrigger(next)
      // Re-arm for the next fire.
      schedule()
    }, delayMs)
  }

  // Auto re-arm whenever any of the watched inputs change. Using
  // separate watches (rather than watchEffect on function calls)
  // keeps the dependency graph explicit and testable.
  const stopEnabledWatch = watch(opts.enabled, schedule, { immediate: true })
  const stopActiveWatch = watch(opts.isActive, schedule)
  const stopIntervalWatch = watch(opts.intervalSeconds, schedule)

  function stop(): void {
    cancel()
    stopEnabledWatch()
    stopActiveWatch()
    stopIntervalWatch()
  }

  onScopeDispose(stop)

  return { stop }
}
