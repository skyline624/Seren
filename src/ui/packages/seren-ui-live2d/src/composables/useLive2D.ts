import { ref, shallowRef } from 'vue'
import * as PIXI from 'pixi.js'

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

  async function init(canvas?: HTMLCanvasElement): Promise<void> {
    const pixiApp = new PIXI.Application({
      width: options.width ?? 400,
      height: options.height ?? 600,
      view: canvas,
      backgroundAlpha: 0,
      antialias: true,
    })
    app.value = pixiApp
  }

  async function loadModel(url: string): Promise<void> {
    if (!app.value) return
    isLoading.value = true
    error.value = null
    try {
      // Dynamic import of pixi-live2d-display
      const { Live2DModel } = await import('pixi-live2d-display')
      const loaded = await Live2DModel.from(url)
      app.value!.stage.addChild(loaded)
      // Center the model
      loaded.x = (app.value!.screen.width - loaded.width) / 2
      loaded.y = 0
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