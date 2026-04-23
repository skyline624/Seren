import { describe, it, expect } from 'vitest'
import {
  createTransformersEmotionClassifier,
  NoopEmotionClassifier,
} from './useTextEmotionClassifier'

/**
 * Worker stub that plays a predetermined script of messages back to the
 * main thread as soon as `postMessage` is called. Avoids spinning up a
 * real Web Worker (jsdom doesn't ship one) and keeps the tests hermetic.
 */
class ScriptedWorker {
  readonly listeners = new Map<string, Array<(event: MessageEvent) => void>>()
  readonly sent: unknown[] = []
  private terminated = false

  constructor(
    /** Reply returned in response to `{ type: 'init' }`. */
    private readonly initReply: unknown,
    /** Reply returned in response to `{ type: 'classify', id }`. */
    private readonly makeClassifyReply: (id: string) => unknown,
  ) {}

  addEventListener(event: string, handler: (e: MessageEvent) => void): void {
    const bucket = this.listeners.get(event) ?? []
    bucket.push(handler)
    this.listeners.set(event, bucket)
  }

  postMessage(msg: unknown): void {
    if (this.terminated) return
    this.sent.push(msg)
    const data = msg as { type: string, id?: string }
    // Dispatch the scripted reply on next microtask so the caller can
    // set up its own Promise state first.
    queueMicrotask(() => {
      if (this.terminated) return
      const bucket = this.listeners.get('message') ?? []
      if (data.type === 'init') {
        const event = { data: this.initReply } as MessageEvent
        for (const fn of bucket) fn(event)
      }
      else if (data.type === 'classify' && data.id) {
        const event = { data: this.makeClassifyReply(data.id) } as MessageEvent
        for (const fn of bucket) fn(event)
      }
    })
  }

  terminate(): void {
    this.terminated = true
  }
}

describe('NoopEmotionClassifier', () => {
  it('returns null from classify regardless of input', async () => {
    const noop = new NoopEmotionClassifier()
    expect(noop.ready.value).toBe(false)
    expect(await noop.classify('anything at all')).toBeNull()
  })

  it('is safe to dispose without init', () => {
    const noop = new NoopEmotionClassifier()
    expect(() => noop.dispose()).not.toThrow()
  })
})

describe('createTransformersEmotionClassifier', () => {
  it('flips ready to true after the worker signals init success', async () => {
    const worker = new ScriptedWorker(
      { type: 'ready' },
      (id: string) => ({ type: 'result', id, predictions: [{ label: 'joy', score: 0.9 }] }),
    )
    const classifier = createTransformersEmotionClassifier({
      confidenceThreshold: 0.6,
      workerFactory: () => worker as unknown as Worker,
    })

    expect(classifier.ready.value).toBe(false)
    await classifier.init()
    expect(classifier.ready.value).toBe(true)
  })

  it('round-trips a classify call through the worker', async () => {
    const worker = new ScriptedWorker(
      { type: 'ready' },
      (id: string) => ({
        type: 'result',
        id,
        predictions: [
          { label: 'joy', score: 0.92 },
          { label: 'sadness', score: 0.04 },
        ],
      }),
    )
    const classifier = createTransformersEmotionClassifier({
      confidenceThreshold: 0.6,
      workerFactory: () => worker as unknown as Worker,
    })
    await classifier.init()

    const result = await classifier.classify('I am so happy today')
    expect(result).not.toBeNull()
    expect(result!.emotion).toBe('joy')
    expect(result!.score).toBeCloseTo(0.92)
  })

  it('returns null when top prediction maps to neutral', async () => {
    const worker = new ScriptedWorker(
      { type: 'ready' },
      (id: string) => ({
        type: 'result',
        id,
        predictions: [{ label: 'neutral', score: 0.95 }],
      }),
    )
    const classifier = createTransformersEmotionClassifier({
      confidenceThreshold: 0.6,
      workerFactory: () => worker as unknown as Worker,
    })
    await classifier.init()

    expect(await classifier.classify('Just a dry factual paragraph.')).toBeNull()
  })

  it('returns null when below confidence threshold', async () => {
    const worker = new ScriptedWorker(
      { type: 'ready' },
      (id: string) => ({
        type: 'result',
        id,
        predictions: [{ label: 'joy', score: 0.4 }],
      }),
    )
    const classifier = createTransformersEmotionClassifier({
      confidenceThreshold: 0.6,
      workerFactory: () => worker as unknown as Worker,
    })
    await classifier.init()

    expect(await classifier.classify('maybe happy?')).toBeNull()
  })

  it('propagates init errors via the returned promise', async () => {
    const worker = new ScriptedWorker(
      { type: 'error', error: 'model download failed' },
      () => ({ type: 'error', id: 'x', error: 'unused' }),
    )
    const classifier = createTransformersEmotionClassifier({
      confidenceThreshold: 0.6,
      workerFactory: () => worker as unknown as Worker,
    })

    await expect(classifier.init()).rejects.toThrow(/model download failed/i)
    expect(classifier.ready.value).toBe(false)
  })

  it('returns null from classify before init completes', async () => {
    const worker = new ScriptedWorker(
      { type: 'ready' },
      (id: string) => ({ type: 'result', id, predictions: [] }),
    )
    const classifier = createTransformersEmotionClassifier({
      confidenceThreshold: 0.6,
      workerFactory: () => worker as unknown as Worker,
    })

    // Without awaiting init(), ready is still false.
    expect(await classifier.classify('anything')).toBeNull()
  })
})
