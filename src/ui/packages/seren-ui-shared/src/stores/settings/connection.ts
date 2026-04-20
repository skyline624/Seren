import { defineStore } from 'pinia'
import { usePersistedRef } from '../../composables/usePersistedRef'

/**
 * WebSocket / REST connection target + auth token. Defaults to the
 * empty string so the app can fall back to same-origin auto-discovery
 * until the user overrides either field from the settings panel.
 */
export const useConnectionSettingsStore = defineStore('settings/connection', () => {
  const serverUrl = usePersistedRef<string>('seren/connection/serverUrl', '')
  const token = usePersistedRef<string>('seren/connection/token', '')

  function reset(): void {
    serverUrl.value = ''
    token.value = ''
  }

  return { serverUrl, token, reset }
})
