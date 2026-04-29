import { acquireAudioStream, releaseStream } from './audioConstraints'
import type { VoiceInputStrategy, VoiceInputStrategyOptions } from './types'

/**
 * Sample rate Silero / Whisper / Parakeet expect on the wire. The PTT
 * recorder captures at the browser's native rate (typically 48 kHz)
 * and we resample down before handing the buffer to <c>onSpeechEnd</c>.
 */
const TARGET_SAMPLE_RATE = 16000

/**
 * Preferred container/codec — <c>audio/webm;codecs=opus</c> is the
 * most widely supported, lossless-enough capture format for speech.
 * Fallback chain mirrors what most browsers ship.
 */
const MIME_TYPE_CANDIDATES = [
  'audio/webm;codecs=opus',
  'audio/webm',
  'audio/ogg;codecs=opus',
  'audio/mp4',
]

/**
 * Push-to-talk strategy — captures raw audio between <c>press</c> and
 * <c>release</c>, decodes the resulting blob, downmixes to mono and
 * resamples to 16 kHz before forwarding to <c>onSpeechEnd</c>. No VAD,
 * no auto speech detection — the user owns the recording window.
 *
 * The MediaStream is acquired once at <c>start</c> and reused for
 * every press (avoids re-prompting for permission and reduces
 * latency). A fresh MediaRecorder is spun up on each press because
 * its <c>dataavailable</c> + <c>stop</c> event sequence ties one
 * recording to one delivery.
 */
export function createPttStrategy(options: VoiceInputStrategyOptions): VoiceInputStrategy {
  let stream: MediaStream | null = null
  let recorder: MediaRecorder | null = null
  let chunks: Blob[] = []
  let mimeType = ''
  let armed = false

  function pickMimeType(): string {
    for (const candidate of MIME_TYPE_CANDIDATES) {
      if (typeof MediaRecorder !== 'undefined' && MediaRecorder.isTypeSupported(candidate)) {
        return candidate
      }
    }
    // Empty string lets the browser pick its own preferred type.
    return ''
  }

  async function start(): Promise<void> {
    if (stream) {
      return
    }
    stream = await acquireAudioStream(options.deviceId, options.audioConstraints)
    mimeType = pickMimeType()
  }

  function press(): void {
    if (!stream || armed) {
      return
    }
    chunks = []
    recorder = mimeType
      ? new MediaRecorder(stream, { mimeType })
      : new MediaRecorder(stream)

    recorder.addEventListener('dataavailable', (event) => {
      if (event.data && event.data.size > 0) {
        chunks.push(event.data)
      }
    })

    recorder.addEventListener('stop', async () => {
      const blob = new Blob(chunks, { type: mimeType || 'audio/webm' })
      chunks = []
      armed = false
      if (blob.size === 0) {
        return
      }
      try {
        const samples = await decodeBlobTo16kMono(blob)
        if (samples.length > 0) {
          options.onSpeechEnd?.(samples)
        }
      }
      catch {
        // Silently drop — caller sees nothing happen but we don't
        // throw across the recorder boundary. Surfacing the error
        // would require a separate channel; v1 keeps it quiet to
        // mirror VAD's onVADMisfire behaviour.
      }
    })

    armed = true
    options.onSpeechStart?.()
    recorder.start()
  }

  function release(): void {
    if (!recorder || recorder.state !== 'recording') {
      armed = false
      return
    }
    recorder.stop()
    // `armed` is reset inside the 'stop' handler so a release that
    // happens before the recorder fully started doesn't leave the
    // strategy stuck.
  }

  function stop(): void {
    if (recorder && recorder.state !== 'inactive') {
      try {
        recorder.stop()
      }
      catch {
        // ignore — we're tearing down
      }
    }
    recorder = null
    chunks = []
    armed = false
    releaseStream(stream)
    stream = null
  }

  return { start, stop, press, release }
}

/**
 * Decodes a recorder blob into a 16 kHz mono Float32Array suitable for
 * the Silero / Whisper / Parakeet pipeline. Uses
 * <c>OfflineAudioContext</c> for resampling so we don't have to ship
 * a custom DSP routine (KISS).
 */
async function decodeBlobTo16kMono(blob: Blob): Promise<Float32Array> {
  const arrayBuffer = await blob.arrayBuffer()

  // A short-lived AudioContext just to decode — closed immediately
  // after to avoid leaking the platform-specific output device.
  const decodeCtx = new AudioContext()
  let buffer: AudioBuffer
  try {
    buffer = await decodeCtx.decodeAudioData(arrayBuffer)
  }
  finally {
    void decodeCtx.close()
  }

  const monoSource = mixToMono(buffer)

  // Browsers won't accept very short OfflineAudioContext lengths
  // (Safari throws under ~256 samples). Pad to a minimum of 1 frame
  // at the target rate just in case.
  const targetLength = Math.max(1, Math.ceil((monoSource.length * TARGET_SAMPLE_RATE) / buffer.sampleRate))
  const offline = new OfflineAudioContext(1, targetLength, TARGET_SAMPLE_RATE)
  const monoBuffer = offline.createBuffer(1, monoSource.length, buffer.sampleRate)
  monoBuffer.getChannelData(0).set(monoSource)
  const src = offline.createBufferSource()
  src.buffer = monoBuffer
  src.connect(offline.destination)
  src.start()

  const rendered = await offline.startRendering()
  // Copy to a freshly-allocated Float32Array so the caller owns the
  // memory (the rendered AudioBuffer stays alive otherwise).
  return new Float32Array(rendered.getChannelData(0))
}

function mixToMono(buffer: AudioBuffer): Float32Array {
  const length = buffer.length
  const mono = new Float32Array(length)

  if (buffer.numberOfChannels === 1) {
    mono.set(buffer.getChannelData(0))
    return mono
  }

  for (let ch = 0; ch < buffer.numberOfChannels; ch++) {
    const channel = buffer.getChannelData(ch)
    for (let i = 0; i < length; i++) {
      mono[i]! += channel[i]!
    }
  }
  const inv = 1 / buffer.numberOfChannels
  for (let i = 0; i < length; i++) {
    mono[i]! *= inv
  }
  return mono
}
