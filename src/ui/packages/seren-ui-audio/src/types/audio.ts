export interface VadWorkerMessage {
  type: 'configure' | 'feed' | 'destroy'
  sampleRate?: number
  threshold?: number
  buffer?: Float32Array
}

export interface VadWorkerResponse {
  type: 'speech-start' | 'speech-end'
  at: number
  audio?: Float32Array
}

export interface VisemeFrame {
  /** Mouth shape identifier (e.g. 'AA', 'EE', 'OH'). */
  viseme: string
  /** Start time in seconds relative to audio start. */
  startTime: number
  /** Duration in seconds. */
  duration: number
  /** Weight 0-1 for blendshape intensity. */
  weight: number
}

export interface AudioChunk {
  audio: Float32Array
  sampleRate: number
  visemes?: VisemeFrame[]
}

export interface PlaybackOptions {
  /** Interrupt current playback when new audio arrives. */
  interruptible?: boolean
  /** Callback for viseme sync. */
  onViseme?: (frame: VisemeFrame) => void
}