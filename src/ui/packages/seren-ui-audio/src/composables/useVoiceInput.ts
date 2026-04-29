import { ref, onUnmounted } from 'vue'
import { createVadStrategy } from '../strategies/vadStrategy'
import { createPttStrategy } from '../strategies/pttStrategy'
import type {
  AudioConstraints,
  VoiceInputStrategy,
  VoiceInputStrategyOptions,
} from '../strategies/types'

/** Sample rate used by Silero VAD (fixed at 16000 Hz). */
export const VAD_SAMPLE_RATE = 16000

/** Voice input strategy mode. <c>'vad'</c> = continuous Silero VAD;
 * <c>'ptt'</c> = manual press-to-talk window. */
export type VoiceInputMode = 'vad' | 'ptt'

export interface UseVoiceInputOptions {
  /** Strategy mode. Defaults to <c>'vad'</c> for backward compat with
   * callers that don't yet know about PTT. */
  mode?: VoiceInputMode
  /** VAD positive speech threshold (VAD only). Default 0.5. */
  threshold?: number
  /** VAD negative speech threshold (VAD only). Default 0.35. */
  negativeSpeechThreshold?: number
  /** Silence frames before <c>onSpeechEnd</c> fires (VAD only). Default 30. */
  redemptionFrames?: number
  /** Microphone deviceId from <c>enumerateDevices</c>. Empty / 'default'
   * lets the OS pick. */
  deviceId?: string
  /** Browser-native audio filters at <c>getUserMedia</c> time. */
  audioConstraints?: AudioConstraints
  /** Fired when speech begins (VAD: Silero crosses positive threshold;
   * PTT: <c>press()</c> is called). */
  onSpeechStart?: () => void
  /** Fired when the recording window closes with the captured audio
   * (16 kHz mono Float32). */
  onSpeechEnd?: (audio: Float32Array) => void
  /** Per-frame Silero "is speech" probability in [0..1] (VAD only). */
  onFrameProgress?: (probability: number) => void
}

/**
 * Returned by <c>useVoiceInput</c>. <c>press</c>/<c>release</c> are
 * present only when <c>mode === 'ptt'</c>; consumers in VAD mode can
 * safely ignore them.
 */
export interface VoiceInputApi {
  isListening: ReturnType<typeof ref<boolean>>
  isMicActive: ReturnType<typeof ref<boolean>>
  error: ReturnType<typeof ref<string | null>>
  start: () => Promise<void>
  stop: () => void
  press?: () => void
  release?: () => void
}

/**
 * Composable orchestrator that selects the right strategy (VAD or PTT)
 * based on <c>options.mode</c>. The composable owns the reactive
 * lifecycle state (<c>isListening</c>, <c>isMicActive</c>, <c>error</c>);
 * the strategies own the technical I/O. Strategy pattern → adding a
 * 3rd mode (e.g. continuous-without-VAD) is a new factory + one
 * <c>switch</c> arm here, no other change (Open/Closed).
 */
export function useVoiceInput(options: UseVoiceInputOptions = {}): VoiceInputApi {
  const isListening = ref(false)
  const isMicActive = ref(false)
  const error = ref<string | null>(null)

  let strategy: VoiceInputStrategy | null = null

  const strategyOptions: VoiceInputStrategyOptions = {
    threshold: options.threshold,
    negativeSpeechThreshold: options.negativeSpeechThreshold,
    redemptionFrames: options.redemptionFrames,
    deviceId: options.deviceId,
    audioConstraints: options.audioConstraints,
    onSpeechStart: () => {
      isListening.value = true
      options.onSpeechStart?.()
    },
    onSpeechEnd: (audio) => {
      isListening.value = false
      options.onSpeechEnd?.(audio)
    },
    onFrameProgress: options.onFrameProgress,
  }

  function buildStrategy(): VoiceInputStrategy {
    if (options.mode === 'ptt') {
      return createPttStrategy(strategyOptions)
    }
    return createVadStrategy(strategyOptions)
  }

  async function start(): Promise<void> {
    try {
      error.value = null
      strategy = buildStrategy()
      await strategy.start()
      isMicActive.value = true
    }
    catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to start voice input'
      isMicActive.value = false
    }
  }

  function stop(): void {
    if (strategy) {
      strategy.stop()
      strategy = null
    }
    isMicActive.value = false
    isListening.value = false
  }

  function press(): void {
    strategy?.press?.()
  }

  function release(): void {
    strategy?.release?.()
  }

  onUnmounted(() => stop())

  // PTT methods are only meaningful when the strategy supports them.
  // Returning them unconditionally keeps the API surface stable for
  // callers (TypeScript discriminates via `mode`); the methods are
  // no-ops when the active strategy doesn't expose them.
  return {
    isListening,
    isMicActive,
    error,
    start,
    stop,
    press,
    release,
  }
}
