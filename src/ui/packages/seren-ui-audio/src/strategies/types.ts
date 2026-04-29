/**
 * Browser-side audio filter knobs piped into <c>MediaTrackConstraints</c>
 * at <c>getUserMedia</c> time. Each is optional — when undefined the
 * platform default applies.
 */
export interface AudioConstraints {
  noiseSuppression?: boolean
  echoCancellation?: boolean
  autoGainControl?: boolean
}

/**
 * Common option bag shared by every voice-input strategy. Strategies
 * pick the subset they need (VAD ignores nothing, PTT ignores Silero
 * thresholds + redemption + onFrameProgress).
 */
export interface VoiceInputStrategyOptions {
  /** Silero VAD positive speech threshold (VAD strategy only). */
  threshold?: number
  /** Silero VAD negative speech threshold (VAD strategy only). */
  negativeSpeechThreshold?: number
  /** Silence frames before <c>onSpeechEnd</c> fires (VAD strategy only). */
  redemptionFrames?: number
  /** Fired when speech begins. */
  onSpeechStart?: () => void
  /** Fired with the final 16 kHz mono Float32 buffer when speech ends. */
  onSpeechEnd?: (audio: Float32Array) => void
  /** Per-frame Silero score callback (VAD strategy only). */
  onFrameProgress?: (probability: number) => void
  /** Microphone device id from <c>navigator.mediaDevices.enumerateDevices</c>. */
  deviceId?: string
  /** Browser-native audio filters applied at <c>getUserMedia</c> time. */
  audioConstraints?: AudioConstraints
}

/**
 * Lifecycle contract every strategy implements (Strategy pattern). VAD
 * strategy uses <c>start</c>/<c>stop</c> only; PTT strategy adds
 * <c>press</c>/<c>release</c> for the click-and-hold interaction.
 */
export interface VoiceInputStrategy {
  /** Acquires the mic + arms the strategy. Idempotent on repeat calls. */
  start: () => Promise<void>
  /** Releases all resources (MediaStream tracks, recorders, recognisers). */
  stop: () => void
  /** PTT only: arms a recording window. No-op for VAD. */
  press?: () => void
  /** PTT only: closes the recording window and emits <c>onSpeechEnd</c>. */
  release?: () => void
}
