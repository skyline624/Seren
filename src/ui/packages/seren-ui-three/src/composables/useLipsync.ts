import type { VRM } from '@pixiv/three-vrm'
import { VRMExpressionPresetName } from '@pixiv/three-vrm'
import { ref } from 'vue'

/**
 * Track of viseme frames produced by the TTS pipeline. Compatible with the
 * `VisemeFrame` record emitted by `@seren/ui-audio` PlaybackManager so the
 * two packages can be chained without a translation layer.
 */
export interface VisemeTrackFrame {
  /** Viseme identifier, e.g. "Aa", "Ih", "Ou", "Ee", "Oh", or "-" for silence. */
  viseme: string
  /** Start time in seconds from the beginning of the parent audio chunk. */
  startTime: number
  /** Duration in seconds. */
  duration: number
  /** Blend weight in [0, 1]. */
  weight: number
}

/**
 * Maps viseme identifiers to the five canonical VRM mouth blendshapes.
 * The silence viseme ("-" or empty) returns null and causes all presets
 * to be reset to 0 — useful for inter-word pauses.
 */
function visemeToPreset(viseme: string): string | null {
  switch (viseme.toLowerCase()) {
    case 'aa':
    case 'a':
      return VRMExpressionPresetName.Aa
    case 'ih':
    case 'i':
      return VRMExpressionPresetName.Ih
    case 'ou':
    case 'u':
      return VRMExpressionPresetName.Ou
    case 'ee':
    case 'e':
      return VRMExpressionPresetName.Ee
    case 'oh':
    case 'o':
      return VRMExpressionPresetName.Oh
    case '-':
    case '':
      return null
    default:
      // Unknown viseme — fall back to "Aa" which is the most neutral open mouth.
      return VRMExpressionPresetName.Aa
  }
}

const MOUTH_PRESETS = [
  VRMExpressionPresetName.Aa,
  VRMExpressionPresetName.Ih,
  VRMExpressionPresetName.Ou,
  VRMExpressionPresetName.Ee,
  VRMExpressionPresetName.Oh,
]

/**
 * Applies a single viseme frame to the VRM expression manager: resets the
 * five mouth presets to 0 then sets the target preset to the frame's
 * weight. Extracted so it can be unit-tested against a mock VRM.
 */
export function applyViseme(vrm: VRM, viseme: string, weight: number): void {
  const manager = vrm.expressionManager
  if (!manager) return

  for (const preset of MOUTH_PRESETS) {
    manager.setValue(preset, 0)
  }

  const target = visemeToPreset(viseme)
  if (target) {
    manager.setValue(target, Math.max(0, Math.min(1, weight)))
  }
}

/**
 * Schedules a track of viseme frames so each one fires its `applyViseme`
 * call at the right moment relative to a time origin. The returned
 * cancel function clears every pending timeout, which is called on
 * interrupt or when a new track starts.
 *
 * The relative clock (not wall time) keeps the caller in control of
 * synchronization — a PlaybackManager with AudioContext.currentTime
 * passes its own elapsed-seconds getter.
 */
export function useLipsync(vrmSource: () => VRM | null) {
  const isActive = ref(false)
  let pendingTimeouts: ReturnType<typeof setTimeout>[] = []

  function clearPending(): void {
    for (const t of pendingTimeouts) clearTimeout(t)
    pendingTimeouts = []
  }

  function playTrack(track: VisemeTrackFrame[]): void {
    clearPending()
    if (track.length === 0) {
      return
    }

    isActive.value = true

    for (const frame of track) {
      const delayMs = Math.max(0, frame.startTime * 1000)
      const timeout = setTimeout(() => {
        const vrm = vrmSource()
        if (vrm) {
          applyViseme(vrm, frame.viseme, frame.weight)
        }
      }, delayMs)
      pendingTimeouts.push(timeout)
    }

    // Schedule a final reset after the last frame so the mouth closes.
    const last = track[track.length - 1]!
    const endMs = Math.max(0, (last.startTime + last.duration) * 1000)
    pendingTimeouts.push(setTimeout(() => {
      const vrm = vrmSource()
      if (vrm) {
        applyViseme(vrm, '-', 0)
      }
      isActive.value = false
    }, endMs))
  }

  function stop(): void {
    clearPending()
    isActive.value = false
    const vrm = vrmSource()
    if (vrm) {
      applyViseme(vrm, '-', 0)
    }
  }

  return {
    isActive,
    playTrack,
    stop,
  }
}
