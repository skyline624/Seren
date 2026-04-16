import { ref, onUnmounted } from 'vue'
import { MicVAD } from '@ricky0123/vad-web'

/** Sample rate used by Silero VAD (fixed at 16000 Hz). */
export const VAD_SAMPLE_RATE = 16000

export interface UseVoiceInputOptions {
  /** VAD positive speech threshold (default 0.5). Maps to positiveSpeechThreshold. */
  threshold?: number
  /** Callback when speech ends with the audio data (16 kHz mono Float32). */
  onSpeechEnd?: (audio: Float32Array) => void
  /** Callback when speech starts. */
  onSpeechStart?: () => void
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