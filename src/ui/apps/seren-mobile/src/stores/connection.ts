import { Preferences } from '@capacitor/preferences'
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

const STORAGE_KEY = 'seren.connection.v1'

export interface ConnectionConfig {
  wsUrl: string
  token: string
}

/**
 * Parses a `seren://connect?ws=...&token=...` URL (payload of the onboarding
 * QR code) into a normalized connection config. Throws on malformed input so
 * callers can show a clear error to the user.
 */
export function parseConnectionQr(raw: string): ConnectionConfig {
  const trimmed = raw.trim()
  if (!trimmed.startsWith('seren://connect')) {
    throw new Error('Invalid Seren QR code: expected seren://connect URL')
  }

  const url = new URL(trimmed)
  const wsUrl = url.searchParams.get('ws')
  const token = url.searchParams.get('token') ?? ''

  if (!wsUrl) {
    throw new Error('Invalid Seren QR code: missing ws parameter')
  }
  if (!/^wss?:\/\//.test(wsUrl)) {
    throw new Error('Invalid Seren QR code: ws must start with ws:// or wss://')
  }

  return { wsUrl, token }
}

export const useConnectionStore = defineStore('connection', () => {
  const config = ref<ConnectionConfig | null>(null)
  const isHydrated = ref(false)

  const isConfigured = computed(() => config.value !== null)

  async function hydrate(): Promise<void> {
    if (isHydrated.value) return
    const { value } = await Preferences.get({ key: STORAGE_KEY })
    if (value) {
      try {
        config.value = JSON.parse(value) as ConnectionConfig
      } catch {
        config.value = null
      }
    }
    isHydrated.value = true
  }

  async function persist(next: ConnectionConfig): Promise<void> {
    config.value = next
    await Preferences.set({ key: STORAGE_KEY, value: JSON.stringify(next) })
  }

  async function setFromQr(raw: string): Promise<ConnectionConfig> {
    const parsed = parseConnectionQr(raw)
    await persist(parsed)
    return parsed
  }

  async function clear(): Promise<void> {
    config.value = null
    await Preferences.remove({ key: STORAGE_KEY })
  }

  return {
    config,
    isHydrated,
    isConfigured,
    hydrate,
    persist,
    setFromQr,
    clear,
  }
})
