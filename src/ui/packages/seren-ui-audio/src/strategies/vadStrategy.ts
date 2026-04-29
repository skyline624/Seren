import { MicVAD } from '@ricky0123/vad-web'
import { acquireAudioStream, releaseStream } from './audioConstraints'
import type { VoiceInputStrategy, VoiceInputStrategyOptions } from './types'

/**
 * Default fallbacks for callers that don't pass thresholds. Mirror the
 * exported defaults in <c>@seren/ui-shared/stores/settings/voice</c>;
 * we re-declare them here only because <c>seren-ui-audio</c> is a leaf
 * package (no dependency on <c>@seren/ui-shared</c>) — keeping the
 * fallback co-located avoids reaching back up the dependency tree.
 */
const FALLBACK_POSITIVE_THRESHOLD = 0.5
const FALLBACK_NEGATIVE_THRESHOLD = 0.35
const FALLBACK_REDEMPTION_FRAMES = 30

/**
 * VAD strategy — wraps Silero MicVAD with our pre-acquired MediaStream
 * (so the user-selected device + filters are honoured rather than
 * MicVAD's internal default <c>getUserMedia</c>).
 *
 * Single Responsibility: this strategy only knows about <i>continuous
 * Silero gating</i>; device acquisition lives in <c>audioConstraints</c>,
 * threshold defaults are caller-provided.
 */
export function createVadStrategy(options: VoiceInputStrategyOptions): VoiceInputStrategy {
  let vad: MicVAD | null = null
  let stream: MediaStream | null = null
  let started = false

  async function start(): Promise<void> {
    if (started) {
      return
    }
    started = true

    stream = await acquireAudioStream(options.deviceId, options.audioConstraints)

    vad = await MicVAD.new({
      stream,
      positiveSpeechThreshold: options.threshold ?? FALLBACK_POSITIVE_THRESHOLD,
      negativeSpeechThreshold: options.negativeSpeechThreshold ?? FALLBACK_NEGATIVE_THRESHOLD,
      redemptionFrames: options.redemptionFrames ?? FALLBACK_REDEMPTION_FRAMES,
      onSpeechStart: () => options.onSpeechStart?.(),
      onSpeechEnd: (audio) => options.onSpeechEnd?.(audio),
      onVADMisfire: () => { /* no-op: caller doesn't care about misfires */ },
      // MicVAD invokes `onFrameProcessed` unconditionally (no null check
      // inside the lib), so we always supply a function — a no-op when
      // the caller hasn't subscribed to per-frame progress, otherwise
      // the bridge to the user-provided `onFrameProgress` callback.
      onFrameProcessed: options.onFrameProgress != null
        ? (probabilities) => options.onFrameProgress!(probabilities.isSpeech)
        : () => { /* no-op — caller doesn't track per-frame probabilities */ },
    })

    vad.start()
  }

  function stop(): void {
    if (vad) {
      vad.destroy()
      vad = null
    }
    releaseStream(stream)
    stream = null
    started = false
  }

  return { start, stop }
}
