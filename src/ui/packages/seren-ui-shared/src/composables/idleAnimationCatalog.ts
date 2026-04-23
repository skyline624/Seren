/**
 * Types + picker for the idle-animation catalog fed to
 * <c>useIdleAnimationScheduler</c>. No shipped default catalog —
 * the catalog is ALWAYS injected by the caller so it stays aligned
 * with the actual <c>.vrma</c> clips registered in their renderer
 * (for Seren: <c>AvatarStage.DEFAULT_IDLE_CLIPS</c>). Adding a new
 * idle = dropping a <c>.vrma</c> and registering one entry in that
 * map — nothing else to touch.
 *
 * Design notes (SRP + OCP + KISS):
 * - Pure data + pure picker. No Vue, no I/O, no stateful refs.
 * - Data-driven: adding a new idle = one entry in the caller's map.
 * - Deterministic: <c>pickNextIdle</c> accepts an injected <c>random()</c>
 *   so tests pin the outcome without patching globals.
 */

/**
 * A single idle variant the scheduler can fire. <see cref="action"/> is
 * the string dispatched through <c>chatStore.currentAction</c>; it must
 * match a name the active renderer knows how to play.
 */
export interface IdleAnimation {
  /** Stable identifier — also used as the `action` payload. */
  id: string

  /** Approximate duration in milliseconds. Only informational today
   *  (the renderer owns real playback timing); reserved for future
   *  cooldown logic so the same animation doesn't fire twice in a row
   *  before it visually finishes. */
  durationMs: number

  /** Relative selection weights per mood. Missing keys fall back to the
   *  `neutral` weight. Weight 0 means "never fire in this mood". */
  moodWeights: Readonly<Record<string, number>>
}

/**
 * Picks the next idle animation using weighted random selection biased
 * by the given mood.
 *
 * Exposed as a pure function so unit tests can verify the distribution
 * with an injected pseudo-random source (e.g. a counter, or a PRNG
 * seeded for the test).
 *
 * @param catalog - animations to choose from. Throws if empty.
 * @param mood - current Seren emotion id (`neutral`, `joy`, `sad`, …).
 *   When `null` or unknown, every weight falls back to the `neutral`
 *   weight (or 1.0 if neither is defined).
 * @param random - `() => number` returning a uniform value in `[0, 1)`.
 *   Defaults to `Math.random`.
 */
export function pickNextIdle(
  catalog: readonly IdleAnimation[],
  mood: string | null,
  random: () => number = Math.random,
): IdleAnimation {
  if (catalog.length === 0) {
    throw new Error('pickNextIdle called with empty catalog')
  }

  const weights = catalog.map(anim => resolveWeight(anim, mood))
  const totalWeight = weights.reduce((sum, w) => sum + w, 0)

  // All zero-weighted? Fall back to uniform selection so the scheduler
  // never hangs — the catalog is still data the user chose to ship.
  if (totalWeight <= 0) {
    const idx = Math.min(catalog.length - 1, Math.floor(random() * catalog.length))
    return catalog[idx]!
  }

  let roll = random() * totalWeight
  for (let i = 0; i < catalog.length; i++) {
    roll -= weights[i]!
    if (roll < 0) {
      return catalog[i]!
    }
  }

  // Floating-point edge: return the last entry if we didn't break above.
  return catalog[catalog.length - 1]!
}

function resolveWeight(anim: IdleAnimation, mood: string | null): number {
  // Explicitly look up so `noUncheckedIndexedAccess` yields the right
  // narrowing — `mood in` by itself isn't enough because Record<string>
  // key access still returns `T | undefined` under strict indexing.
  if (mood !== null) {
    const byMood = anim.moodWeights[mood]
    if (byMood !== undefined) {
      return Math.max(0, byMood)
    }
  }
  const neutral = anim.moodWeights.neutral
  if (neutral !== undefined) {
    return Math.max(0, neutral)
  }
  return 1
}
