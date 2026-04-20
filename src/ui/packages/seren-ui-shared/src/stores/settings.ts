import { defineStore, storeToRefs } from 'pinia'
import { useAppearanceSettingsStore } from './settings/appearance'
import { useAvatarSettingsStore } from './settings/avatar'
import { useConnectionSettingsStore } from './settings/connection'
import { useLlmSettingsStore } from './settings/llm'
import { useVoiceSettingsStore } from './settings/voice'

// Legacy-shaped fields kept for backward compatibility. Consumers
// (chat.ts, App.vue, SettingsPanel.vue) still read these; new code
// should go directly through the sub-stores under `./settings/`.
export interface SerenSettings {
  serverUrl: string
  token: string
  language: 'fr' | 'en'
  avatarMode: 'vrm' | 'live2d'
  vadThreshold: number
  llmProvider?: string
  llmModel?: string
}

/**
 * Umbrella store that re-exposes each sub-store's state under the
 * legacy field names. The reset helper cascades to every sub-store.
 */
export const useSettingsStore = defineStore('settings', () => {
  const connection = useConnectionSettingsStore()
  const appearance = useAppearanceSettingsStore()
  const avatar = useAvatarSettingsStore()
  const voice = useVoiceSettingsStore()
  const llm = useLlmSettingsStore()

  const { serverUrl, token } = storeToRefs(connection)
  const { locale: language, themeMode, primaryHue } = storeToRefs(appearance)
  const { mode: avatarMode } = storeToRefs(avatar)
  const { vadThreshold } = storeToRefs(voice)
  const { provider: llmProvider, model: llmModel, thinkingMode } = storeToRefs(llm)

  function reset(): void {
    connection.reset()
    appearance.reset()
    avatar.reset()
    voice.reset()
    llm.reset()
  }

  return {
    serverUrl,
    token,
    language,
    themeMode,
    primaryHue,
    avatarMode,
    vadThreshold,
    llmProvider,
    llmModel,
    thinkingMode,
    reset,
  }
})

export { useAppearanceSettingsStore } from './settings/appearance'
export { useAvatarSettingsStore } from './settings/avatar'
export { useConnectionSettingsStore } from './settings/connection'
export { useLlmSettingsStore } from './settings/llm'
export { useVoiceSettingsStore } from './settings/voice'
