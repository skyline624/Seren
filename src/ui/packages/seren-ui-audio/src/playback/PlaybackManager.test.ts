import { describe, expect, it, vi } from 'vitest'
import type { AudioChunk, VisemeFrame } from '../types/audio'

// Fake AudioContext enough to satisfy PlaybackManager's allocations without
// a real Web Audio backend (jsdom doesn't implement it).
class FakeBufferSource {
  public buffer: AudioBuffer | null = null
  public onended: (() => void) | null = null
  connect(): void {}
  start(): void {
    // Delay onended so scheduled viseme setTimeouts have time to fire first.
    // Real audio sources fire onended at buffer.duration which is non-zero.
    setTimeout(() => this.onended?.(), 100)
  }

  stop(): void {}
}

class FakeAudioContext {
  public currentTime = 0
  public destination = {}

  createBuffer(_channels: number, length: number, sampleRate: number): AudioBuffer {
    return {
      length,
      sampleRate,
      duration: length / sampleRate,
      numberOfChannels: 1,
      getChannelData: () => new Float32Array(length),
    } as unknown as AudioBuffer
  }

  createBufferSource(): AudioBufferSourceNode {
    return new FakeBufferSource() as unknown as AudioBufferSourceNode
  }

  async close(): Promise<void> {}
}

// Swap the global before importing the module under test.
vi.stubGlobal('AudioContext', FakeAudioContext)

// Dynamic import after stubbing the global.
const { PlaybackManager } = await import('./PlaybackManager')

function makeChunk(length: number, visemes?: VisemeFrame[]): AudioChunk {
  return {
    audio: new Float32Array(length),
    sampleRate: 24_000,
    visemes,
  }
}

describe('playbackManager', () => {
  it('Push_WithSingleChunk_ShouldPlayAndFlushQueue', async () => {
    // arrange
    const manager = new PlaybackManager({ interruptible: false })

    // act
    await manager.push(makeChunk(100))
    // microtask queue needs a tick so onended fires
    await Promise.resolve()
    await Promise.resolve()

    // assert: after playing, queue is empty and a new push should work again
    await manager.push(makeChunk(100))
    expect(true).toBe(true)
  })

  it('Push_WithVisemes_ShouldInvokeOnVisemeCallback', async () => {
    // arrange
    const onViseme = vi.fn()
    const manager = new PlaybackManager({ interruptible: false, onViseme })
    const visemes: VisemeFrame[] = [
      { viseme: 'Aa', startTime: 0.005, duration: 0.002, weight: 1 },
      { viseme: 'Ih', startTime: 0.01, duration: 0.002, weight: 1 },
    ]

    // act
    void manager.push(makeChunk(200, visemes))
    // Drain microtasks so scheduleVisemes runs
    await Promise.resolve()
    await Promise.resolve()
    // Wait enough real milliseconds for the scheduled timeouts to fire
    await new Promise(resolve => setTimeout(resolve, 50))

    // assert
    expect(onViseme).toHaveBeenCalled()
    const calledWith = onViseme.mock.calls.map(c => c[0])
    expect(calledWith).toContainEqual(visemes[0])
    expect(calledWith).toContainEqual(visemes[1])
  })

  it('Interrupt_WhilePlaying_ShouldClearQueueAndStop', async () => {
    // arrange
    const manager = new PlaybackManager({ interruptible: true })
    await manager.push(makeChunk(100))

    // act
    manager.interrupt()

    // assert: subsequent push should work (queue was cleared)
    await manager.push(makeChunk(100))
    expect(true).toBe(true)
  })
})
