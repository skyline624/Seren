import { ref, onUnmounted } from 'vue'
import { MicVAD } from '@ricky0123/vad-web'

/** Sample rate used by Silero VAD (fixed at 16000 Hz). */
export const VAD_SAMPLE_RATE = 16000

/**
 * Default neutral fallback for the negative threshold when the caller
 * doesn't pass one. Mirrors {@link NEGATIVE_THRESHOLD_DEFAULT} in
 * `@seren/ui-shared`; we re-declare it here to keep `seren-ui-audio`
 * free of cross-package dependency cycles. Tests assert both stay in
 * sync.
 */
const FALLBACK_NEGATIVE_SPEECH_THRESHOLD = 0.35

/** Default redemption frame window when the caller doesn't pass one. */
const FALLBACK_REDEMPTION_FRAMES = 30

export interface UseVoiceInputOptions {
  /**
   * VAD positive speech threshold — Silero score above which the engine
   * flips to "speech in progress". Maps to MicVAD's
   * <c>positiveSpeechThreshold</c>. Default 0.5.
   */
  threshold?: number
  /**
   * VAD negative speech threshold — Silero score below which the engine
   * starts the redemption countdown back to "silence". Must be lower
   * than {@link threshold} to be useful (otherwise the VAD never
   * transitions to silence). Default 0.35.
   */
  negativeSpeechThreshold?: number
  /**
   * Number of consecutive silence frames after a speech burst before
   * <c>onSpeechEnd</c> fires. Silero ships at 32 ms per frame; the
   * default of 30 ≈ 960 ms covers natural mid-sentence breaths in
   * conversational French.
   */
  redemptionFrames?: number
  /** Callback when speech ends with the audio data (16 kHz mono Float32). */
  onSpeechEnd?: (audio: Float32Array) => void
  /** Callback when speech starts. */
  onSpeechStart?: () => void
  /**
   * Optional progress callback fired on every Silero frame (~32 ms) with
   * the raw "is speech" probability in [0..1]. Used by the calibration
   * VU-meter in the settings tab; unset by default so chat consumers
   * don't pay the per-frame closure cost.
   */
  onFrameProgress?: (probability: number) => void
}

export function useVoiceInput(options: UseVoiceInputOptions = {}) {
  const isListening = ref(false)
  const isMicActive = ref(false)
  const error = ref<string | null>(null)
  let vad: MicVAD | null = null

  async function start(): Promise<void> {
    try {
      error.value = null
      vad = await MicVAD.new({
        positiveSpeechThreshold: options.threshold ?? 0.5,
        negativeSpeechThreshold: options.negativeSpeechThreshold ?? FALLBACK_NEGATIVE_SPEECH_THRESHOLD,
        redemptionFrames: options.redemptionFrames ?? FALLBACK_REDEMPTION_FRAMES,
        onSpeechStart: () => {
          isListening.value = true
          options.onSpeechStart?.()
        },
        onSpeechEnd: (audio) => {
          isListening.value = false
          options.onSpeechEnd?.(audio)
        },
        onVADMisfire: () => {
          isListening.value = false
        },
        onFrameProcessed: options.onFrameProgress != null
          ? (probabilities) => {
              // Silero's `isSpeech` is the raw [0..1] head score we
              // surface for calibration; `notSpeech` is its complement.
              options.onFrameProgress!(probabilities.isSpeech)
            }
          : undefined,
      })
      vad.start()
      isMicActive.value = true
    }
    catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to start voice input'
    }
  }

  function stop(): void {
    if (vad) {
      vad.destroy()
      vad = null
    }
    isMicActive.value = false
    isListening.value = false
  }

  onUnmounted(() => stop())

  return {
    isListening,
    isMicActive,
    error,
    start,
    stop,
  }
}
