import { beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { AVATAR_DEFAULTS, useAvatarSettingsStore } from './avatar'

beforeAll(() => {
  if (typeof globalThis.localStorage !== 'undefined') return
  const store = new Map<string, string>()
  globalThis.localStorage = {
    get length() { return store.size },
    clear: () => store.clear(),
    getItem: (k: string) => store.get(k) ?? null,
    key: (i: number) => Array.from(store.keys())[i] ?? null,
    removeItem: (k: string) => { store.delete(k) },
    setItem: (k: string, v: string) => { store.set(k, String(v)) },
  } as Storage
})

describe('useAvatarSettingsStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('returns default values when nothing is persisted', () => {
    const store = useAvatarSettingsStore()
    expect(store.mode).toBe(AVATAR_DEFAULTS.mode)
    expect(store.modelScale).toBe(AVATAR_DEFAULTS.modelScale)
    expect(store.cameraFov).toBe(AVATAR_DEFAULTS.cameraFov)
    expect(store.eyeTrackingMode).toBe(AVATAR_DEFAULTS.eyeTrackingMode)
    expect(store.outlineColor).toBe(AVATAR_DEFAULTS.outlineColor)
    expect(store.rotationY).toBeNull()
  })

  it('persists a mutation under its dedicated key', async () => {
    const store = useAvatarSettingsStore()
    store.modelScale = 1.5
    store.eyeTrackingMode = 'pointer'
    await nextTick()
    expect(JSON.parse(localStorage.getItem('seren/avatar/modelScale')!)).toBe(1.5)
    expect(JSON.parse(localStorage.getItem('seren/avatar/eyeTrackingMode')!)).toBe('pointer')
  })

  it('rehydrates persisted values on a fresh store instance', () => {
    localStorage.setItem('seren/avatar/cameraDistance', JSON.stringify(2.5))
    localStorage.setItem('seren/avatar/outlineColor', JSON.stringify('#ff00aa'))

    const store = useAvatarSettingsStore()
    expect(store.cameraDistance).toBe(2.5)
    expect(store.outlineColor).toBe('#ff00aa')
  })

  it('reset() cascades every knob back to its default', () => {
    const store = useAvatarSettingsStore()
    store.modelScale = 0.7
    store.cameraFov = 80
    store.outlineColor = '#ff0000'
    store.eyeTrackingMode = 'off'
    store.rotationY = 90

    store.reset()

    expect(store.modelScale).toBe(AVATAR_DEFAULTS.modelScale)
    expect(store.cameraFov).toBe(AVATAR_DEFAULTS.cameraFov)
    expect(store.outlineColor).toBe(AVATAR_DEFAULTS.outlineColor)
    expect(store.eyeTrackingMode).toBe(AVATAR_DEFAULTS.eyeTrackingMode)
    expect(store.rotationY).toBeNull()
  })

  it('supports explicit rotationY override while nullable by default', async () => {
    const store = useAvatarSettingsStore()
    expect(store.rotationY).toBeNull()
    store.rotationY = 45
    await nextTick()
    expect(JSON.parse(localStorage.getItem('seren/avatar/rotationY')!)).toBe(45)
  })
})
