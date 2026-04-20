import { beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import { LEGACY_BLOB_MAP, usePersistedRef } from './usePersistedRef'

const MIGRATION_FLAG_KEY = 'seren/migrated/v1'
const LEGACY_BLOB_KEY = 'seren-settings'

// Vitest runs in Node by default and has no DOM — stub just enough of
// localStorage for the composable under test.
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

describe('usePersistedRef', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('returns the default value when the key is missing', () => {
    const ref = usePersistedRef('seren/test/missing', 'default')
    expect(ref.value).toBe('default')
  })

  it('hydrates from localStorage when the key exists', () => {
    localStorage.setItem('seren/test/present', JSON.stringify('stored'))
    const ref = usePersistedRef('seren/test/present', 'default')
    expect(ref.value).toBe('stored')
  })

  it('persists writes to localStorage after mutation', async () => {
    const ref = usePersistedRef('seren/test/write', 0)
    ref.value = 42
    await nextTick()
    expect(JSON.parse(localStorage.getItem('seren/test/write')!)).toBe(42)
  })

  it('falls back to the default on malformed stored JSON', () => {
    localStorage.setItem('seren/test/corrupt', '{not-json')
    const ref = usePersistedRef('seren/test/corrupt', { a: 1 })
    expect(ref.value).toEqual({ a: 1 })
  })

  it('persists deep mutations on object values', async () => {
    const ref = usePersistedRef<{ a: number, nested: { b: number } }>(
      'seren/test/deep',
      { a: 1, nested: { b: 2 } },
    )
    ref.value.nested.b = 99
    await nextTick()
    const stored = JSON.parse(localStorage.getItem('seren/test/deep')!) as { nested: { b: number } }
    expect(stored.nested.b).toBe(99)
  })
})

describe('usePersistedRef legacy migration', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('splits the legacy blob into per-key entries on first access', () => {
    localStorage.setItem(LEGACY_BLOB_KEY, JSON.stringify({
      serverUrl: 'ws://localhost:5080/ws',
      token: 'dev-token',
      language: 'en',
      avatarMode: 'live2d',
      vadThreshold: 0.7,
      llmProvider: 'ollama',
      llmModel: 'ollama/seren-qwen',
    }))

    // First access triggers migration.
    usePersistedRef('seren/connection/serverUrl', '')

    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.serverUrl)!)).toBe('ws://localhost:5080/ws')
    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.token)!)).toBe('dev-token')
    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.language)!)).toBe('en')
    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.avatarMode)!)).toBe('live2d')
    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.vadThreshold)!)).toBe(0.7)
    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.llmProvider)!)).toBe('ollama')
    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.llmModel)!)).toBe('ollama/seren-qwen')
    expect(localStorage.getItem(LEGACY_BLOB_KEY)).toBeNull()
    expect(localStorage.getItem(MIGRATION_FLAG_KEY)).toBe('true')
  })

  it('does not overwrite a new key that already has a value', () => {
    localStorage.setItem(LEGACY_BLOB_KEY, JSON.stringify({ serverUrl: 'legacy-url' }))
    localStorage.setItem(LEGACY_BLOB_MAP.serverUrl, JSON.stringify('newer-url'))

    usePersistedRef('seren/connection/serverUrl', '')

    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.serverUrl)!)).toBe('newer-url')
    expect(localStorage.getItem(LEGACY_BLOB_KEY)).toBeNull()
  })

  it('is idempotent — a second run leaves keys untouched', () => {
    localStorage.setItem(LEGACY_BLOB_KEY, JSON.stringify({ serverUrl: 'first' }))

    usePersistedRef('seren/connection/serverUrl', '')
    // Simulate a user edit between invocations.
    localStorage.setItem(LEGACY_BLOB_MAP.serverUrl, JSON.stringify('edited-by-user'))
    usePersistedRef('seren/connection/serverUrl', '')

    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.serverUrl)!)).toBe('edited-by-user')
  })

  it('flags completion even when the legacy blob is absent', () => {
    usePersistedRef('seren/test/any', 0)
    expect(localStorage.getItem(MIGRATION_FLAG_KEY)).toBe('true')
  })

  it('drops a corrupt legacy blob without throwing', () => {
    localStorage.setItem(LEGACY_BLOB_KEY, '{not-json')
    usePersistedRef('seren/test/any', 0)
    expect(localStorage.getItem(LEGACY_BLOB_KEY)).toBeNull()
    expect(localStorage.getItem(MIGRATION_FLAG_KEY)).toBe('true')
  })

  it('ignores missing or null fields in the legacy blob', () => {
    localStorage.setItem(LEGACY_BLOB_KEY, JSON.stringify({
      serverUrl: 'only-this',
      token: null,
      llmProvider: undefined,
    }))
    usePersistedRef('seren/test/any', 0)
    expect(JSON.parse(localStorage.getItem(LEGACY_BLOB_MAP.serverUrl)!)).toBe('only-this')
    expect(localStorage.getItem(LEGACY_BLOB_MAP.token)).toBeNull()
    expect(localStorage.getItem(LEGACY_BLOB_MAP.llmProvider)).toBeNull()
  })
})
