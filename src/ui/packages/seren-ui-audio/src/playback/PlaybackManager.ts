import type { AudioChunk, PlaybackOptions, VisemeFrame } from '../types/audio'

export class PlaybackManager {
  private audioContext: AudioContext
  private queue: AudioChunk[] = []
  private isPlaying = false
  private currentSource: AudioBufferSourceNode | null = null
  private currentStartTime = 0
  private options: Required<PlaybackOptions>
  private visemeTimeouts: ReturnType<typeof setTimeout>[] = []

  constructor(options: PlaybackOptions = {}) {
    this.audioContext = new AudioContext()
    this.options = {
      interruptible: options.interruptible ?? true,
      onViseme: options.onViseme ?? (() => {}),
    }
  }

  async push(chunk: AudioChunk): Promise<void> {
    if (this.options.interruptible && this.isPlaying) {
      this.interrupt()
    }
    this.queue.push(chunk)
    if (!this.isPlaying) {
      await this.playNext()
    }
  }

  interrupt(): void {
    if (this.currentSource) {
      this.currentSource.stop()
      this.currentSource = null
    }
    this.clearVisemeTimeouts()
    this.isPlaying = false
    this.queue = []
  }

  private async playNext(): Promise<void> {
    const chunk = this.queue.shift()
    if (!chunk) {
      this.isPlaying = false
      return
    }

    this.isPlaying = true
    const buffer = this.audioContext.createBuffer(1, chunk.audio.length, chunk.sampleRate)
    buffer.getChannelData(0).set(chunk.audio)

    const source = this.audioContext.createBufferSource()
    source.buffer = buffer
    source.connect(this.audioContext.destination)
    this.currentSource = source
    this.currentStartTime = this.audioContext.currentTime

    // Schedule viseme callbacks
    if (chunk.visemes) {
      this.scheduleVisemes(chunk.visemes)
    }

    return new Promise<void>((resolve) => {
      source.onended = () => {
        this.currentSource = null
        this.isPlaying = false
        this.clearVisemeTimeouts()
        this.playNext().then(resolve)
      }
      source.start()
    })
  }

  private scheduleVisemes(visemes: VisemeFrame[]): void {
    this.clearVisemeTimeouts()
    for (const frame of visemes) {
      const delay = (frame.startTime - (this.audioContext.currentTime - this.currentStartTime)) * 1000
      if (delay > 0) {
        const timeout = setTimeout(() => {
          this.options.onViseme(frame)
        }, delay)
        this.visemeTimeouts.push(timeout)
      }
    }
  }

  private clearVisemeTimeouts(): void {
    for (const t of this.visemeTimeouts) clearTimeout(t)
    this.visemeTimeouts = []
  }

  destroy(): void {
    this.interrupt()
    this.audioContext.close()
  }
}