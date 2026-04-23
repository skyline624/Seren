import { ref } from 'vue'
import type { Ref } from 'vue'
import type { ClassifierLabel, SerenEmotion } from './textEmotionMapping'
import { mapClassifierPredictionsToSeren } from './textEmotionMapping'

/** Default HuggingFace model id. Small (~66 MB quantised ONNX) + covers
 *  7 English emotions. Overridable via constructor for
 *  experimentation. */
export const DEFAULT_EMOTION_MODEL = 'Xenova/distilbert-base-uncased-emotion'

/** Result of a classifier call. `null` means "no confident emotion
 *  detected" — callers should NOT fire an `currentEmotion` event in
 *  that case (otherwise every `neutral` classification would animate
 *  the avatar into a stone face). */
export interface EmotionPrediction {
  emotion: SerenEmotion
  score: number
}

/**
 * Narrow interface the chat store consumes. One concrete impl per
 * strategy (real transformers.js worker vs. no-op). Enables
 * DI-driven mocks in tests and a clean kill-switch when the user
 * toggles the feature off or the model fails to load.
 */
export interface ITextEmotionClassifier {
  /** True once the model is loaded and ready to classify. */
  readonly ready: Ref<boolean>
  /** Last non-null error from init / classify. Null while healthy. */
  readonly lastError: Ref<string | null>
  /** Start the classifier. Idempotent; safe to call multiple times. */
  init: () => Promise<void>
  /** Classify a chunk of text. Returns `null` when below confidence
   *  threshold, when not ready, or when the instance is disabled. */
  classify: (text: string) => Promise<EmotionPrediction | null>
  /** Tear down the underlying resources (terminate worker). */
  dispose: () => void
}

/**
 * Interchangeable no-op classifier. Used when the settings flag is
 * off, as the initial value before lazy-init, and in tests that don't
 * care about actual predictions.
 */
export class NoopEmotionClassifier implements ITextEmotionClassifier {
  readonly ready = ref(false)
  readonly lastError = ref<string | null>(null)

  async init(): Promise<void> { /* no-op */ }
  // Keep the same signature as the real classifier (takes `text`) so
  // consumers can treat both as the same interface without narrowing.
  // eslint-disable-next-line unused-imports/no-unused-vars
  async classify(_text: string): Promise<null> { return null }
  dispose(): void { /* no-op */ }
}

/**
 * Factory that builds the transformers.js-backed classifier inside a
 * Web Worker. Kept as a factory (not a class) because it captures a
 * handful of private refs and closures — the caller treats it as an
 * opaque `ITextEmotionClassifier`.
 *
 * Parameters :
 * - `confidenceThreshold` — minimum score before we map to a non-null
 *   emotion. Below threshold → returns `null` so the chat store
 *   doesn't fire `currentEmotion`.
 * - `modelId` — HuggingFace model id, defaults to
 *   `DEFAULT_EMOTION_MODEL`.
 * - `workerFactory` — injected for tests; returns a freshly-constructed
 *   `Worker`. Defaults to spawning the real worker with
 *   `new Worker(new URL(...), { type: 'module' })`.
 */
export function createTransformersEmotionClassifier(opts: {
  confidenceThreshold: number
  modelId?: string
  workerFactory?: () => Worker
}): ITextEmotionClassifier {
  const modelId = opts.modelId ?? DEFAULT_EMOTION_MODEL
  const ready = ref(false)
  const lastError = ref<string | null>(null)

  let worker: Worker | null = null
  let initPromise: Promise<void> | null = null
  const pending = new Map<string, {
    resolve: (p: EmotionPrediction | null) => void
    reject: (e: Error) => void
  }>()

  function handleMessage(event: MessageEvent): void {
    const msg = event.data as
      | { type: 'ready' }
      | { type: 'error', id?: string, error: string }
      | { type: 'result', id: string, predictions: ClassifierLabel[] }

    if (msg.type === 'ready') {
      ready.value = true
      lastError.value = null
      return
    }

    if (msg.type === 'error') {
      lastError.value = msg.error
      if (msg.id) {
        const entry = pending.get(msg.id)
        if (entry) {
          pending.delete(msg.id)
          entry.reject(new Error(msg.error))
        }
      }
      else {
        // Init-time error : mark the classifier unusable.
        ready.value = false
      }
      return
    }

    if (msg.type === 'result') {
      const entry = pending.get(msg.id)
      if (!entry) return
      pending.delete(msg.id)
      const prediction = mapPredictionsToEmotion(msg.predictions, opts.confidenceThreshold)
      entry.resolve(prediction)
    }
  }

  function init(): Promise<void> {
    if (initPromise) return initPromise
    initPromise = new Promise<void>((resolve, reject) => {
      try {
        worker = opts.workerFactory
          ? opts.workerFactory()
          : new Worker(new URL('../workers/emotion-classifier.worker.ts', import.meta.url), {
              type: 'module',
            })
      }
      catch (err) {
        const message = err instanceof Error ? err.message : String(err)
        lastError.value = message
        reject(new Error(message))
        return
      }

      const w = worker
      const onMessage = (event: MessageEvent): void => {
        handleMessage(event)
        const data = event.data as { type: string, error?: string }
        if (data.type === 'ready') {
          resolve()
        }
        else if (data.type === 'error' && !('id' in data)) {
          reject(new Error(data.error ?? 'classifier failed to initialise'))
        }
      }
      w.addEventListener('message', onMessage)
      w.addEventListener('error', (ev) => {
        lastError.value = ev.message
        ready.value = false
      })

      w.postMessage({ type: 'init', modelId })
    })
    return initPromise
  }

  async function classify(text: string): Promise<EmotionPrediction | null> {
    if (!ready.value || !worker) return null
    if (text.length === 0) return null

    const id = generateId()
    return new Promise<EmotionPrediction | null>((resolve, reject) => {
      pending.set(id, { resolve, reject })
      worker!.postMessage({ type: 'classify', id, text })
    })
  }

  function dispose(): void {
    if (worker) {
      worker.terminate()
      worker = null
    }
    ready.value = false
    initPromise = null
    for (const entry of pending.values()) {
      entry.resolve(null)
    }
    pending.clear()
  }

  return { ready, lastError, init, classify, dispose }
}

function mapPredictionsToEmotion(
  predictions: readonly ClassifierLabel[],
  threshold: number,
): EmotionPrediction | null {
  if (predictions.length === 0) return null
  const emotion = mapClassifierPredictionsToSeren(predictions, threshold)
  if (emotion === 'neutral') {
    // Treat neutral as "no signal" — avoid animating a blank face over
    // every chunk. The LLM markers remain the authoritative channel.
    return null
  }
  const topScore = predictions[0]?.score ?? 0
  return { emotion, score: topScore }
}

function generateId(): string {
  // Cryptographically-random ids not needed — we only correlate
  // requests to the same worker inside a single tab.
  return Math.random().toString(36).slice(2, 10) + Date.now().toString(36)
}
