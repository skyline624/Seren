import { ref, shallowRef } from 'vue'
import * as PIXI from 'pixi.js'

// pixi-live2d-display accesses PIXI.Ticker via the global `window.PIXI`
// object when registering its shared ticker. Expose our bundled PIXI so the
// Cubism4 runtime can patch it correctly.
declare global {
  interface Window {
    PIXI?: typeof PIXI
  }
}
if (typeof window !== 'undefined' && !window.PIXI) {
  window.PIXI = PIXI
}

// Emotion-to-expression mapping
export const EMOTION_MAP: Record<string, { group?: string; index?: number; expression?: string }> = {
  joy: { expression: 'happy' },
  happy: { expression: 'happy' },
  anger: { expression: 'angry' },
  angry: { expression: 'angry' },
  sorrow: { expression: 'sad' },
  sad: { expression: 'sad' },
  surprise: { expression: 'surprised' },
  surprised: { expression: 'surprised' },
  neutral: { expression: 'neutral' },
  // Motion-based emotions
  wave: { group: 'tap body', index: 0 },
  nod: { group: 'tap body', index: 1 },
}

export interface UseLive2DOptions {
  canvas?: HTMLCanvasElement
  width?: number
  height?: number
}

export function useLive2D(options: UseLive2DOptions = {}) {
  const app = shallowRef<PIXI.Application | null>(null)
  const model = shallowRef<InstanceType<typeof import('pixi-live2d-display').Live2DModel> | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const currentEmotion = ref<string>('neutral')

  async function init(
    canvas?: HTMLCanvasElement,
    initOptions: { width?: number; height?: number } = {},
  ): Promise<void> {
    const pixiApp = new PIXI.Application({
      width: initOptions.width ?? options.width ?? 400,
      height: initOptions.height ?? options.height ?? 600,
      view: canvas,
      backgroundAlpha: 0,
      antialias: true,
      autoDensity: true,
      resolution: globalThis.devicePixelRatio ?? 1,
      // Keep the drawing buffer around so tools (screenshots, GIF export,
      // tests reading pixels) can inspect the rendered frame at any time.
      // Trades a small perf hit for a much better dev/test experience.
      preserveDrawingBuffer: true,
    })
    app.value = pixiApp
  }

  async function loadModel(url: string): Promise<void> {
    if (!app.value) return
    isLoading.value = true
    error.value = null
    try {
      // Use the Cubism 4 runtime — our bundled default model (Hiyori) is
      // distributed as .moc3 which is a Cubism 4 asset. Importing the
      // default `pixi-live2d-display` entry only ships the Cubism 2 runtime
      // and throws "Could not find Cubism 2 runtime" for moc3 models.
      const { Live2DModel } = await import('pixi-live2d-display/cubism4')
      const loaded = await Live2DModel.from(url)
      app.value!.stage.addChild(loaded)
      // Fit the model to the canvas while preserving aspect ratio. Use the
      // raw (pre-scale) dimensions to compute `scale`; after applying it the
      // model's `.width` / `.height` already reflect the final rendered size,
      // so multiplying by `scale` again would over-shrink the layout.
      const { screen } = app.value!
      const rawWidth = loaded.width
      const rawHeight = loaded.height
      const scale = Math.min(
        screen.width / rawWidth,
        screen.height / rawHeight,
      ) * 0.9 // 10 % margin so the head/feet don't sit flush with the edges
      loaded.scale.set(scale)
      loaded.x = (screen.width - loaded.width) / 2
      loaded.y = (screen.height - loaded.height) / 2
      model.value = loaded
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load Live2D model'
    } finally {
      isLoading.value = false
    }
  }

  function setEmotion(emotion: string): void {
    if (!model.value) return
    const mapping = EMOTION_MAP[emotion.toLowerCase()]
    if (!mapping) return

    if (mapping.expression) {
      model.value.expression(mapping.expression)
    }
    if (mapping.group !== undefined && mapping.index !== undefined) {
      model.value.motion(mapping.group, mapping.index)
    }
    currentEmotion.value = emotion
  }

  function setFocus(x: number, y: number): void {
    model.value?.focus(x, y)
  }

  function dispose(): void {
    if (model.value) {
      app.value?.stage.removeChild(model.value)
      model.value = null
    }
    app.value?.destroy(true)
    app.value = null
  }

  return {
    app,
    model,
    isLoading,
    error,
    currentEmotion,
    init,
    loadModel,
    setEmotion,
    setFocus,
    dispose,
  }
}