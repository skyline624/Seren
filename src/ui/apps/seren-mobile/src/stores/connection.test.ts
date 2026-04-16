import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// Fake Capacitor Preferences plugin — the real one throws when not on a
// native platform.
vi.mock('@capacitor/preferences', () => {
  const store = new Map<string, string>()
  return {
    Preferences: {
      get: async ({ key }: { key: string }) => ({ value: store.get(key) ?? null }),
      set: async ({ key, value }: { key: string, value: string }) => {
        store.set(key, value)
      },
      remove: async ({ key }: { key: string }) => {
        store.delete(key)
      },
    },
  }
})

const { parseConnectionQr, useConnectionStore } = await import('./connection')

describe('parseConnectionQr', () => {
  it('Parse_WithValidSerenUrl_ShouldReturnNormalizedConfig', () => {
    // arrange
    const raw = 'seren://connect?ws=wss://hub.example.com/ws&token=abc123'

    // act
    const config = parseConnectionQr(raw)

    // assert
    expect(config).toEqual({ wsUrl: 'wss://hub.example.com/ws', token: 'abc123' })
  })

  it('Parse_WithoutToken_ShouldDefaultToEmptyString', () => {
    // arrange
    const raw = 'seren://connect?ws=ws://localhost:5000/ws'

    // act
    const config = parseConnectionQr(raw)

    // assert
    expect(config.token).toBe('')
  })

  it('Parse_WithWrongScheme_ShouldThrow', () => {
    expect(() => parseConnectionQr('https://example.com'))
      .toThrowError(/seren:\/\/connect/)
  })

  it('Parse_WithoutWsParam_ShouldThrow', () => {
    expect(() => parseConnectionQr('seren://connect?token=abc'))
      .toThrowError(/missing ws parameter/)
  })

  it('Parse_WithNonWsUrl_ShouldThrow', () => {
    expect(() => parseConnectionQr('seren://connect?ws=https://example.com'))
      .toThrowError(/ws must start with ws:\/\/ or wss:\/\//)
  })
})

describe('useConnectionStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('Hydrate_WhenNothingPersisted_ShouldStayUnconfigured', async () => {
    // arrange
    const store = useConnectionStore()

    // act
    await store.hydrate()

    // assert
    expect(store.isHydrated).toBe(true)
    expect(store.isConfigured).toBe(false)
  })

  it('SetFromQr_WithValidUrl_ShouldPersistAndMarkConfigured', async () => {
    // arrange
    const store = useConnectionStore()

    // act
    await store.setFromQr('seren://connect?ws=wss://h/ws&token=t')

    // assert
    expect(store.isConfigured).toBe(true)
    expect(store.config?.wsUrl).toBe('wss://h/ws')
    expect(store.config?.token).toBe('t')
  })

  it('Clear_AfterConfigured_ShouldResetState', async () => {
    // arrange
    const store = useConnectionStore()
    await store.setFromQr('seren://connect?ws=wss://h/ws')

    // act
    await store.clear()

    // assert
    expect(store.isConfigured).toBe(false)
    expect(store.config).toBeNull()
  })
})
