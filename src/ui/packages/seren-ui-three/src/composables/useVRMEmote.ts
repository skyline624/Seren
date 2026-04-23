import type { VRM } from '@pixiv/three-vrm'

/**
 * Emotion blendshape manager for a VRM avatar. Given a semantic
 * emotion name (`joy`, `sad`, `anger`, `surprise`, `relaxed`,
 * `neutral`), blend the VRM expression manager toward the matching
 * preset with a cubic ease + optional auto-reset.
 *
 * Design (SRP + OCP) :
 *  - Emotion → expression mapping lives in {@link DEFAULT_EMOTIONS}
 *    (Readonly). Adding an emotion = one entry, no code change.
 *  - Name aliases live in {@link EMOTION_ALIASES} so the hub can
 *    emit `happy`, `angry`, etc. and still resolve to our canonical
 *    ids (also Readonly / data-driven).
 *  - Canonical source : pattern ported from AIRI `useVRMEmote` in
 *    `stage-ui-three/expression.ts`. Matches VRM blendshape preset
 *    names (`happy`, `sad`, `angry`, `surprised`, `relaxed`,
 *    `neutral`) used by both VRM 0.x (capitalized by three-vrm) and
 *    VRM 1.0 (lowercased).
 *  - Writes exclusively to `VRMExpressionManager.setValue(name, v)` —
 *    no bone mutation. Complies with the Face layer responsibility.
 *
 * Testability : the caller passes `now` + `setTimer`/`clearTimer` to
 * swap the clock in tests. Defaults fall through to native time.
 */

export interface EmotionExpressionEntry {
  name: string
  value: number
}

export interface EmotionPreset {
  expression: readonly EmotionExpressionEntry[]
  /** Blend duration in seconds for the ease-in transition. */
  blendDuration: number
}

/**
 * Map of semantic emotion id → VRM blendshape presets. Values are
 * the peak weights reached at the end of the blend. `neutral` is
 * intentionally lower because the baseline avatar is already neutral.
 */
export const DEFAULT_EMOTIONS: Readonly<Record<string, EmotionPreset>> = Object.freeze({
  neutral: { expression: [{ name: 'neutral', value: 0.2 }], blendDuration: 0.3 },
  joy: { expression: [{ name: 'happy', value: 0.7 }], blendDuration: 0.3 },
  sad: { expression: [{ name: 'sad', value: 0.7 }], blendDuration: 0.4 },
  anger: { expression: [{ name: 'angry', value: 0.7 }], blendDuration: 0.3 },
  surprise: { expression: [{ name: 'surprised', value: 0.8 }], blendDuration: 0.2 },
  relaxed: { expression: [{ name: 'relaxed', value: 0.5 }], blendDuration: 0.4 },
})

/**
 * Alias table mapping LLM / hub emotion strings to the canonical
 * keys of {@link DEFAULT_EMOTIONS}. Forgiving lookup — unknown
 * strings fall back to `neutral`.
 */
export const EMOTION_ALIASES: Readonly<Record<string, string>> = Object.freeze({
  // joy family
  joy: 'joy',
  happy: 'joy',
  happiness: 'joy',
  // anger family
  anger: 'anger',
  angry: 'anger',
  mad: 'anger',
  // sad family
  sad: 'sad',
  sadness: 'sad',
  depressed: 'sad',
  // surprise family
  surprise: 'surprise',
  surprised: 'surprise',
  shocked: 'surprise',
  // relaxed family
  relaxed: 'relaxed',
  calm: 'relaxed',
  serene: 'relaxed',
  // neutral
  neutral: 'neutral',
  none: 'neutral',
})

/** Resolve a potentially-aliased emotion name to a canonical id. */
export function resolveEmotionKey(name: string | null | undefined): string | null {
  if (!name) return null
  return EMOTION_ALIASES[name.toLowerCase()] ?? null
}

export interface VRMEmoteOptions {
  /** Emotion map override. Defaults to {@link DEFAULT_EMOTIONS}. */
  emotions?: Readonly<Record<string, EmotionPreset>>
  /** `Date.now()`-equivalent in ms — injected for deterministic tests. */
  now?: () => number
  /** `setTimeout`-equivalent — injected for deterministic tests. */
  setTimer?: (fn: () => void, ms: number) => ReturnType<typeof setTimeout>
  /** `clearTimeout`-equivalent — injected for deterministic tests. */
  clearTimer?: (handle: ReturnType<typeof setTimeout>) => void
  /**
   * Debug callback (gated log). Called on set, reset, and unknown
   * emotion. No-op by default. The caller wires it to
   * `avatarDebugLog('emote', …)` if needed.
   */
  onDebug?: (event: string, details: Record<string, unknown>) => void
}

export interface VRMEmoteController {
  /**
   * Set the active emotion and start blending toward it. Unknown
   * names fall back to `neutral` (with a debug log). `intensity`
   * scales the peak weights from the preset — default 1.0.
   */
  setEmotion: (name: string, intensity?: number) => void
  /** Like `setEmotion`, but schedule a reset to `neutral` after `ms`. */
  setEmotionWithResetAfter: (name: string, ms: number, intensity?: number) => void
  /** Per-frame tick : advance the blend and write to the VRM. */
  update: (delta: number) => void
  /** Cancel pending reset timer + clear transient state. */
  dispose: () => void
}

/**
 * Cubic ease-in-out used by AIRI and most game engines for emotion
 * blends. Smooth acceleration + symmetric deceleration.
 */
function easeInOutCubic(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2
}

export function useVRMEmote(
  vrmRef: () => VRM | null | undefined,
  opts: VRMEmoteOptions = {},
): VRMEmoteController {
  const emotions = opts.emotions ?? DEFAULT_EMOTIONS
  const setTimer = opts.setTimer ?? ((fn, ms) => setTimeout(fn, ms))
  const clearTimer = opts.clearTimer ?? ((h) => clearTimeout(h))
  const onDebug = opts.onDebug ?? (() => { })

  // Current per-expression weights currently being written.
  const current = new Map<string, number>()
  // Where each expression is blending TO.
  const target = new Map<string, number>()
  // Where each expression started blending FROM (for lerp).
  const start = new Map<string, number>()

  let blendDuration = 0
  let blendElapsed = 0
  let isBlending = false
  let resetTimer: ReturnType<typeof setTimeout> | null = null

  function setEmotion(name: string, intensity = 1): void {
    const key = resolveEmotionKey(name)
    const resolvedKey = key ?? 'neutral'
    if (key === null) {
      onDebug('unknown', { requested: name, fallback: 'neutral' })
    }

    const preset = emotions[resolvedKey]
    if (!preset) {
      onDebug('missing-preset', { name: resolvedKey })
      return
    }

    // Snapshot current as start so the lerp picks up from whatever we
    // were writing just before. Expressions not in the new preset
    // blend DOWN to 0 (so emotions don't stack over time).
    for (const [expressionName, currentValue] of current) {
      start.set(expressionName, currentValue)
    }
    target.clear()
    for (const entry of preset.expression) {
      const v = entry.value * Math.max(0, Math.min(1, intensity))
      target.set(entry.name, v)
      if (!start.has(entry.name)) start.set(entry.name, 0)
    }
    // Expressions in start but not in new target → blend to 0.
    for (const startKey of start.keys()) {
      if (!target.has(startKey)) target.set(startKey, 0)
    }

    blendDuration = Math.max(0.001, preset.blendDuration)
    blendElapsed = 0
    isBlending = true
    onDebug('set', { name: resolvedKey, intensity })
  }

  function setEmotionWithResetAfter(name: string, ms: number, intensity = 1): void {
    setEmotion(name, intensity)
    if (resetTimer !== null) clearTimer(resetTimer)
    resetTimer = setTimer(() => {
      resetTimer = null
      setEmotion('neutral', 1)
      onDebug('reset', { from: name })
    }, ms)
  }

  function update(_delta: number): void {
    if (!isBlending) return

    const vrm = vrmRef()
    const manager = vrm?.expressionManager
    if (!manager) return

    blendElapsed += _delta
    const t = Math.min(1, blendElapsed / blendDuration)
    const eased = easeInOutCubic(t)

    for (const [name, toValue] of target) {
      const fromValue = start.get(name) ?? 0
      const v = fromValue + (toValue - fromValue) * eased
      current.set(name, v)
      manager.setValue(name, v)
    }

    if (t >= 1) {
      isBlending = false
      // Clean up entries that blended down to exactly 0 to keep maps
      // small on long sessions.
      for (const [name, v] of current) {
        if (v === 0) {
          current.delete(name)
          start.delete(name)
          target.delete(name)
        }
      }
    }
  }

  function dispose(): void {
    if (resetTimer !== null) {
      clearTimer(resetTimer)
      resetTimer = null
    }
    isBlending = false
    current.clear()
    target.clear()
    start.clear()
  }

  return { setEmotion, setEmotionWithResetAfter, update, dispose }
}
