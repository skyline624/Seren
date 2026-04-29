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
 * Sentinel for "let the OS pick the input device". The browser's
 * <c>getUserMedia</c> treats an unset <c>deviceId</c> as "default";
 * we expose it as the explicit string <c>"default"</c> so the UI
 * dropdown has a stable value to render.
 */
export const SELECTED_DEVICE_DEFAULT = 'default'

/** Voice input strategy. <c>'vad'</c> = continuous Silero VAD with
 * auto speech-end detection; <c>'ptt'</c> = manual press-to-talk on the
 * mic button.
 */
export type VoiceInputMode = 'vad' | 'ptt'

/** Default mode: continuous VAD (current behaviour, easier first-use). */
export const INPUT_MODE_DEFAULT: VoiceInputMode = 'vad'

/**
 * Default browser-native audio filters. Mirrors the reasonable defaults
 * exposed by Chromium / Firefox / Safari for a microphone capture:
 * suppression de bruit + écho activés (gros bénéfice qualitatif), AGC
 * désactivé (préserve la dynamique de la voix, évite le pumping audio
 * que Whisper et Parakeet aiment moins).
 */
export const NOISE_SUPPRESSION_DEFAULT = true
export const ECHO_CANCELLATION_DEFAULT = true
export const AUTO_GAIN_CONTROL_DEFAULT = false

/**
 * Voice input pipeline settings. The store is the single source of truth
 * for VAD calibration + Whisper language preference + microphone
 * acquisition options; both ChatPanel and the VoxMind settings tab read
 * from here. Defaults are exported as named constants so consumers can
 * reuse them in tests / composables without re-declaring magic numbers
 * (DRY).
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

  const selectedDeviceId = usePersistedRef<string>(
    'seren/voice/deviceId',
    SELECTED_DEVICE_DEFAULT,
  )

  const inputMode = usePersistedRef<VoiceInputMode>(
    'seren/voice/inputMode',
    INPUT_MODE_DEFAULT,
  )

  const noiseSuppression = usePersistedRef<boolean>(
    'seren/voice/noiseSuppression',
    NOISE_SUPPRESSION_DEFAULT,
  )

  const echoCancellation = usePersistedRef<boolean>(
    'seren/voice/echoCancellation',
    ECHO_CANCELLATION_DEFAULT,
  )

  const autoGainControl = usePersistedRef<boolean>(
    'seren/voice/autoGainControl',
    AUTO_GAIN_CONTROL_DEFAULT,
  )

  function reset(): void {
    vadThreshold.value = POSITIVE_THRESHOLD_DEFAULT
    negativeSpeechThreshold.value = NEGATIVE_THRESHOLD_DEFAULT
    redemptionFrames.value = REDEMPTION_FRAMES_DEFAULT
    sttLanguage.value = STT_LANGUAGE_DEFAULT
    selectedDeviceId.value = SELECTED_DEVICE_DEFAULT
    inputMode.value = INPUT_MODE_DEFAULT
    noiseSuppression.value = NOISE_SUPPRESSION_DEFAULT
    echoCancellation.value = ECHO_CANCELLATION_DEFAULT
    autoGainControl.value = AUTO_GAIN_CONTROL_DEFAULT
  }

  return {
    vadThreshold,
    negativeSpeechThreshold,
    redemptionFrames,
    sttLanguage,
    selectedDeviceId,
    inputMode,
    noiseSuppression,
    echoCancellation,
    autoGainControl,
    reset,
  }
})
