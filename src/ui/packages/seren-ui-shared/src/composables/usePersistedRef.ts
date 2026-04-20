import { ref, watch, type Ref } from 'vue'

// Flag written to localStorage the first time the legacy monolithic
// `seren-settings` blob is split into per-key entries. Lets us rerun the
// app without re-migrating (which would overwrite user tweaks made since).
const MIGRATION_FLAG_KEY = 'seren/migrated/v1'
const LEGACY_BLOB_KEY = 'seren-settings'

// Mapping between fields of the old monolithic blob and the new per-key
// localStorage layout. Exported for tests; consumed by `runLegacyMigration`.
// `as const` is important here: it narrows each value to its literal
// string type so test code can read `LEGACY_BLOB_MAP.serverUrl` without
// TypeScript inferring `string | undefined` under noUncheckedIndexedAccess.
export const LEGACY_BLOB_MAP = {
  serverUrl: 'seren/connection/serverUrl',
  token: 'seren/connection/token',
  language: 'seren/appearance/locale',
  themeMode: 'seren/appearance/themeMode',
  primaryHue: 'seren/appearance/primaryHue',
  avatarMode: 'seren/avatar/mode',
  vadThreshold: 'seren/voice/vadThreshold',
  llmProvider: 'seren/llm/provider',
  llmModel: 'seren/llm/model',
} as const

/**
 * Idempotent one-shot migration of the legacy `'seren-settings'` JSON
 * blob into the new per-key layout. Called lazily on the first access
 * of `usePersistedRef`, so it runs before any sub-store reads its own
 * key and gets `null` where the legacy value should have been.
 */
function runLegacyMigration(): void {
  if (typeof localStorage === 'undefined') return
  if (localStorage.getItem(MIGRATION_FLAG_KEY) === 'true') return

  const raw = localStorage.getItem(LEGACY_BLOB_KEY)
  if (!raw) {
    localStorage.setItem(MIGRATION_FLAG_KEY, 'true')
    return
  }

  try {
    const blob = JSON.parse(raw) as Record<string, unknown>
    for (const [legacyField, newKey] of Object.entries(LEGACY_BLOB_MAP)) {
      const value = blob[legacyField]
      if (value === undefined || value === null) continue
      if (localStorage.getItem(newKey) !== null) continue
      localStorage.setItem(newKey, JSON.stringify(value))
    }
    localStorage.removeItem(LEGACY_BLOB_KEY)
  }
  catch {
    // Corrupt legacy blob — drop it; the app will start on defaults.
    localStorage.removeItem(LEGACY_BLOB_KEY)
  }
  localStorage.setItem(MIGRATION_FLAG_KEY, 'true')
}

/**
 * A `Ref<T>` whose value is transparently mirrored to `localStorage`
 * under the given key. Reads fall back to `defaultValue` when the key
 * is missing or its payload fails to parse. Writes stringify the current
 * value and ignore storage failures (SSR, private-mode quota, etc.).
 *
 * The returned ref is deep-watched so mutating nested properties on
 * object/array values also persists.
 */
export function usePersistedRef<T>(key: string, defaultValue: T): Ref<T> {
  runLegacyMigration()

  const initial = readFromStorage<T>(key, defaultValue)
  const state = ref(initial) as Ref<T>

  watch(state, (next) => {
    writeToStorage(key, next)
  }, { deep: true })

  return state
}

function readFromStorage<T>(key: string, defaultValue: T): T {
  if (typeof localStorage === 'undefined') return defaultValue
  try {
    const raw = localStorage.getItem(key)
    if (raw === null) return defaultValue
    return JSON.parse(raw) as T
  }
  catch {
    return defaultValue
  }
}

function writeToStorage<T>(key: string, value: T): void {
  if (typeof localStorage === 'undefined') return
  try {
    localStorage.setItem(key, JSON.stringify(value))
  }
  catch {
    // storage unavailable (SSR) or quota exceeded — silently skip
  }
}
