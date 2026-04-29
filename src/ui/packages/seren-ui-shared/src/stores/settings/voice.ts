import { defineStore } from 'pinia'
import { usePersistedRef } from '../../composables/usePersistedRef'

/**
 * Default VAD positive speech threshold (Silero score above which the VAD
 * flips to "speech in progress").
 */
export const POSITIVE_THRESHOLD_DEFAULT = 0.5

/**
 * Default VAD negative speech threshold (Silero score below which the VAD
 * starts the redemption countdown back to "silence"). Bumped from the
 * legacy hard-coded 0.30 — typical room noise was holding the score in
 * the 0.30–0.40 band, preventing the redemption frames counter from ever
 * reaching its target.
 */
export const NEGATIVE_THRESHOLD_DEFAULT = 0.35

/**
 * Default redemption frame count (Silero ships at 32 ms / frame, so 30
 * frames ≈ 960 ms of trailing silence required before <c>onSpeechEnd</c>
 * fires).
 */
export const REDEMPTION_FRAMES_DEFAULT = 30

/**
 * Number of milliseconds covered by a single Silero frame at 16 kHz with
 * 512-sample windows (constant of the upstream model — exposed so UI
 * components can render redemption frames in milliseconds without
 * reaching for the magic number).
 */
export const SILERO_FRAME_MS = 32

/** Allowed Whisper STT language hints surfaced from the UI. */
export type SttLanguage = 'auto' | 'fr' | 'en'

/** Default Whisper language: let sherpa-onnx auto-detect. */
export const STT_LANGUAGE_DEFAULT: SttLanguage = 'auto'

/**
 * Voice input pipeline settings. The store is the single source of truth
 * for VAD calibration + Whisper language preference; both ChatPanel and
 * the VoxMind settings tab read from here. Defaults are exported as
 * named constants so consumers can reuse them in tests / composables
 * without re-declaring magic numbers (DRY).
 */
export const useVoiceSettingsStore = defineStore('settings/voice', () => {
  const vadThreshold = usePersistedRef<number>(
    'seren/voice/vadThreshold',
    POSITIVE_THRESHOLD_DEFAULT,
  )

  const negativeSpeechThreshold = usePersistedRef<number>(
    'seren/voice/negativeSpeechThreshold',
    NEGATIVE_THRESHOLD_DEFAULT,
  )

  const redemptionFrames = usePersistedRef<number>(
    'seren/voice/redemptionFrames',
    REDEMPTION_FRAMES_DEFAULT,
  )

  const sttLanguage = usePersistedRef<SttLanguage>(
    'seren/voice/sttLanguage',
    STT_LANGUAGE_DEFAULT,
  )

  function reset(): void {
    vadThreshold.value = POSITIVE_THRESHOLD_DEFAULT
    negativeSpeechThreshold.value = NEGATIVE_THRESHOLD_DEFAULT
    redemptionFrames.value = REDEMPTION_FRAMES_DEFAULT
    sttLanguage.value = STT_LANGUAGE_DEFAULT
  }

  return {
    vadThreshold,
    negativeSpeechThreshold,
    redemptionFrames,
    sttLanguage,
    reset,
  }
})
