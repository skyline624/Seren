import { defineStore } from 'pinia'
import { computed } from 'vue'
import { usePersistedRef } from '../../composables/usePersistedRef'

/** Human-facing pace of idle animations. Mapped to min/max second
 *  intervals in the scheduler — label first, numbers derived, so we can
 *  tune the curve later without touching localStorage payloads. */
export type IdleFrequency = 'slow' | 'normal' | 'fast'

/**
 * Interval windows (seconds) applied by the idle scheduler based on the
 * user-facing frequency label. Exposed for unit tests and for the
 * scheduler composable (DRY: one source of truth, not duplicated in the
 * scheduler).
 */
export const IDLE_FREQUENCY_INTERVALS: Readonly<Record<IdleFrequency, readonly [number, number]>>
  = Object.freeze({
    slow: [30, 60],
    normal: [15, 35],
    fast: [8, 18],
  })

export const ANIMATION_DEFAULTS = Object.freeze({
  idleEnabled: true,
  idleFrequency: 'normal' as IdleFrequency,
  classifierEnabled: false, // opt-in — ~66 MB download
  classifierConfidenceThreshold: 0.6,
  // Phase 1 face layer + Phase 2 body layer — on by default so the
  // avatar feels alive out of the box. Per-toggle opt-out for users
  // who prefer a statue (or to debug the base clip in isolation).
  blinkEnabled: true,
  saccadeEnabled: true,
  bodySwayEnabled: true,
})

/**
 * Tunables for the client-side avatar AI: idle animation scheduler
 * (Tier 1) and in-browser text emotion classifier (Tier 2). Every field
 * persists to its own localStorage key via `usePersistedRef` — toggling
 * one knob never rewrites the others.
 */
export const useAnimationSettingsStore = defineStore('settings/animation', () => {
  const idleEnabled = usePersistedRef<boolean>(
    'seren/animation/idleEnabled',
    ANIMATION_DEFAULTS.idleEnabled,
  )

  const idleFrequency = usePersistedRef<IdleFrequency>(
    'seren/animation/idleFrequency',
    ANIMATION_DEFAULTS.idleFrequency,
  )

  const classifierEnabled = usePersistedRef<boolean>(
    'seren/animation/classifierEnabled',
    ANIMATION_DEFAULTS.classifierEnabled,
  )

  const classifierConfidenceThreshold = usePersistedRef<number>(
    'seren/animation/classifierConfidenceThreshold',
    ANIMATION_DEFAULTS.classifierConfidenceThreshold,
  )

  const blinkEnabled = usePersistedRef<boolean>(
    'seren/animation/blinkEnabled',
    ANIMATION_DEFAULTS.blinkEnabled,
  )

  const saccadeEnabled = usePersistedRef<boolean>(
    'seren/animation/saccadeEnabled',
    ANIMATION_DEFAULTS.saccadeEnabled,
  )

  const bodySwayEnabled = usePersistedRef<boolean>(
    'seren/animation/bodySwayEnabled',
    ANIMATION_DEFAULTS.bodySwayEnabled,
  )

  /** Derived `[minSeconds, maxSeconds]` matching the current frequency label. */
  const idleIntervalSeconds = computed<readonly [number, number]>(
    () => IDLE_FREQUENCY_INTERVALS[idleFrequency.value],
  )

  function reset(): void {
    idleEnabled.value = ANIMATION_DEFAULTS.idleEnabled
    idleFrequency.value = ANIMATION_DEFAULTS.idleFrequency
    classifierEnabled.value = ANIMATION_DEFAULTS.classifierEnabled
    classifierConfidenceThreshold.value = ANIMATION_DEFAULTS.classifierConfidenceThreshold
    blinkEnabled.value = ANIMATION_DEFAULTS.blinkEnabled
    saccadeEnabled.value = ANIMATION_DEFAULTS.saccadeEnabled
    bodySwayEnabled.value = ANIMATION_DEFAULTS.bodySwayEnabled
  }

  return {
    idleEnabled,
    idleFrequency,
    classifierEnabled,
    classifierConfidenceThreshold,
    blinkEnabled,
    saccadeEnabled,
    bodySwayEnabled,
    idleIntervalSeconds,
    reset,
  }
})
