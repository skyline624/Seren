/// <reference lib="webworker" />

/**
 * Web Worker wrapping `@huggingface/transformers` text-classification
 * pipeline. Runs in total isolation from the main thread so classifier
 * inference can never cause a dropped frame on the render loop.
 *
 * Protocol (main ↔ worker):
 *
 * ```
 *   main → worker:  { type: 'init',     modelId: string }
 *   main ← worker:  { type: 'ready' }                        on success
 *   main ← worker:  { type: 'error',    error: string }      on any failure
 *
 *   main → worker:  { type: 'classify', id: string, text: string }
 *   main ← worker:  { type: 'result',   id: string, predictions: [{label, score}] }
 *   main ← worker:  { type: 'error',    id: string, error: string }
 * ```
 *
 * The `id` field correlates requests ↔ results so the main-thread
 * composable can resolve the right Promise without serialising calls.
 *
 * Kept intentionally tiny (~60 lines) — any growth probably means a
 * new responsibility that deserves its own worker.
 */

import { pipeline } from '@huggingface/transformers'

// eslint-disable-next-line ts/no-explicit-any
type ClassifierPipeline = (text: string, opts?: Record<string, unknown>) => Promise<any>

let classifier: ClassifierPipeline | null = null

// Type assertion: inside a worker module, `self` is `DedicatedWorkerGlobalScope`.
const workerSelf = self as unknown as DedicatedWorkerGlobalScope

workerSelf.addEventListener('message', async (event: MessageEvent) => {
  const msg = event.data as
    | { type: 'init', modelId: string }
    | { type: 'classify', id: string, text: string }

  if (msg.type === 'init') {
    try {
      // The transformers.js `pipeline()` overloads produce a union
      // type across dozens of task kinds that vue-tsc flags as "too
      // complex to represent". Double-cast through `unknown` breaks
      // that inference chain — the call site is already narrowed to
      // `'text-classification'` so the runtime shape is guaranteed.
      const built = (await pipeline('text-classification', msg.modelId)) as unknown
      classifier = built as ClassifierPipeline
      workerSelf.postMessage({ type: 'ready' })
    }
    catch (err) {
      workerSelf.postMessage({
        type: 'error',
        error: err instanceof Error ? err.message : String(err),
      })
    }
    return
  }

  if (msg.type === 'classify') {
    if (!classifier) {
      workerSelf.postMessage({ type: 'error', id: msg.id, error: 'classifier not initialised' })
      return
    }
    try {
      // `top_k: null` returns every class with its score, sorted desc.
      const raw = await classifier(msg.text, { top_k: null })
      workerSelf.postMessage({ type: 'result', id: msg.id, predictions: raw })
    }
    catch (err) {
      workerSelf.postMessage({
        type: 'error',
        id: msg.id,
        error: err instanceof Error ? err.message : String(err),
      })
    }
  }
})
