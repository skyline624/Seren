import type { Live2DModel } from 'pixi-live2d-display'
import { ref } from 'vue'

/**
 * Track of viseme frames produced by the TTS pipeline. Same shape as the
 * VRM-side `VisemeTrackFrame` (declared in @seren/ui-three/useLipsync) —
 * duplicated here to avoid cross-package coupling. Structural compatibility
 * keeps both paths interchangeable from AvatarStage.
 */
export interface VisemeTrackFrame {
  /** Viseme identifier, e.g. "aa", "ih", "ou", "ee", "oh", or "-" for silence. */
  viseme: string
  /** Start time in seconds from the beginning of the parent audio chunk. */
  startTime: number
  /** Duration in seconds. */
  duration: number
  /** Blend weight in [0, 1]. */
  weight: number
}

/** Cubism 4 mouth parameter ids present on the bundled Hiyori model. */
const PARAM_MOUTH_OPEN = 'ParamMouthOpenY'
const PARAM_MOUTH_FORM = 'ParamMouthForm'

/** Subset of the Cubism 4 core model we actually need. Declared inline so
 * the composable has no hard dependency on pixi-live2d-display internals. */
interface CoreModelLike {
  setParameterValueById(id: string, value: number): void
}

/**
 * Maps a viseme identifier to the `ParamMouthForm` value (-1 rounded,
 * 0 neutral, +1 spread). Lowercases its input; unknown visemes default
 * to neutral.
 */
export function visemeToMouthForm(viseme: string): number {
  const v = viseme.toLowerCase()
  if (v === 'ee' || v === 'e') return 1
  if (v === 'ou' || v === 'u' || v === 'oh' || v === 'o') return -1
  return 0
}

/**
 * Applies a single viseme frame to the Live2D core model. Extracted so
 * it can be unit-tested against a plain mock without PIXI.
 */
export function applyVisemeLive2D(
  core: CoreModelLike,
  viseme: string,
  weight: number,
): void {
  const clamped = Math.max(0, Math.min(1, weight))
  const isSilence = viseme === '-' || viseme === ''
  core.setParameterValueById(PARAM_MOUTH_OPEN, isSilence ? 0 : clamped)
  core.setParameterValueById(PARAM_MOUTH_FORM, isSilence ? 0 : visemeToMouthForm(viseme))
}

/**
 * Live2D counterpart of `useLipsync` from @seren/ui-three. Schedules
 * viseme frames via `setTimeout` and drives `ParamMouthOpenY` /
 * `ParamMouthForm` on the Cubism 4 core model.
 */
export function useLive2DLipsync(modelSource: () => Live2DModel | null) {
  const isActive = ref(false)
  let pendingTimeouts: ReturnType<typeof setTimeout>[] = []

  function coreModel(): CoreModelLike | null {
    const model = modelSource() as
      | (Live2DModel & { internalModel?: { coreModel?: CoreModelLike } })
      | null
    // The `internalModel.coreModel` surface is only re-exported by the
    // /cubism4 entry point — declare it structurally so we can drive the
    // mouth parameters without pulling a subpath-specific type.
    return model?.internalModel?.coreModel ?? null
  }

  function clearPending(): void {
    for (const t of pendingTimeouts) clearTimeout(t)
    pendingTimeouts = []
  }

  function playTrack(track: VisemeTrackFrame[]): void {
    clearPending()
    if (track.length === 0) return

    isActive.value = true

    for (const frame of track) {
      const delayMs = Math.max(0, frame.startTime * 1000)
      const timeout = setTimeout(() => {
        const core = coreModel()
        if (core) applyVisemeLive2D(core, frame.viseme, frame.weight)
      }, delayMs)
      pendingTimeouts.push(timeout)
    }

    // Reset the mouth to rest after the last frame so the avatar doesn't
    // stay mid-vowel.
    const last = track[track.length - 1]!
    const endMs = Math.max(0, (last.startTime + last.duration) * 1000)
    pendingTimeouts.push(setTimeout(() => {
      const core = coreModel()
      if (core) applyVisemeLive2D(core, '-', 0)
      isActive.value = false
    }, endMs))
  }

  function stop(): void {
    clearPending()
    isActive.value = false
    const core = coreModel()
    if (core) applyVisemeLive2D(core, '-', 0)
  }

  return {
    isActive,
    playTrack,
    stop,
  }
}
