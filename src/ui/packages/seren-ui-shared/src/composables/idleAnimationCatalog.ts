/**
 * Catalog of idle animation variants the scheduler can fire during
 * pauses (nobody typing, nothing streaming).
 *
 * Design notes (SRP + OCP + KISS):
 * - **Pure data**: no Vue, no I/O, no stateful refs. One source of truth.
 * - **Extensible**: add a new animation = one entry in the catalog,
 *   no `switch` to touch anywhere. Adding a new mood = extend
 *   `moodWeights` — missing keys fall back to the `neutral` weight.
 * - **Deterministic**: `pickWeighted` accepts an injected `random()` so
 *   unit tests pin the outcome without monkey-patching globals.
 * - **Renderer-agnostic**: each entry maps to an `action` id that both
 *   VRM and Live2D can consume through their existing `currentAction`
 *   channel. Today's set happens to map 1:1 to VRM gesture names + a
 *   handful of new head-pose variants (`look_left`, `look_right`,
 *   `look_up`, `blink_double`, `breath_deep`). Live2D falls back to
 *   its Idle motion group indices when a literal gesture isn't available.
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
 * Default catalog shipped with Seren. Seven variants keep the avatar
 * visibly alive without looking manic. Weights bias the selection
 * towards mood-coherent motions: a sad character looks down more,
 * a joyful one looks up / stretches more.
 */
export const DEFAULT_IDLE_CATALOG: readonly IdleAnimation[] = Object.freeze([
  {
    id: 'look_left',
    durationMs: 1500,
    moodWeights: { neutral: 1.0, joy: 0.8, sad: 0.5, anger: 0.7, surprise: 0.6, relaxed: 0.9 },
  },
  {
    id: 'look_right',
    durationMs: 1500,
    moodWeights: { neutral: 1.0, joy: 0.8, sad: 0.5, anger: 0.7, surprise: 0.6, relaxed: 0.9 },
  },
  {
    id: 'look_up',
    durationMs: 1200,
    moodWeights: { neutral: 0.7, joy: 1.2, sad: 0.2, anger: 0.3, surprise: 1.0, relaxed: 0.8 },
  },
  {
    id: 'look_down',
    durationMs: 1200,
    moodWeights: { neutral: 0.6, joy: 0.3, sad: 1.3, anger: 0.5, surprise: 0.3, relaxed: 0.6 },
  },
  {
    id: 'blink_double',
    durationMs: 600,
    moodWeights: { neutral: 1.0, joy: 0.9, sad: 0.8, anger: 0.9, surprise: 1.2, relaxed: 1.0 },
  },
  {
    id: 'breath_deep',
    durationMs: 2000,
    moodWeights: { neutral: 0.9, joy: 0.8, sad: 1.0, anger: 0.7, surprise: 0.5, relaxed: 1.2 },
  },
  {
    id: 'stretch_small',
    durationMs: 1800,
    moodWeights: { neutral: 0.7, joy: 1.0, sad: 0.2, anger: 0.3, surprise: 0.4, relaxed: 1.1 },
  },
])

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
