import { defineStore, storeToRefs } from 'pinia'
import { useAppearanceSettingsStore } from './settings/appearance'
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
  vadThreshold: number
  llmModel?: string
}

/**
 * Umbrella store that re-exposes each sub-store's state under the
 * legacy field names. The reset helper cascades to every sub-store.
 */
export const useSettingsStore = defineStore('settings', () => {
  const connection = useConnectionSettingsStore()
  const appearance = useAppearanceSettingsStore()
  const voice = useVoiceSettingsStore()
  const llm = useLlmSettingsStore()

  const { serverUrl, token } = storeToRefs(connection)
  const { locale: language, themeMode, primaryHue } = storeToRefs(appearance)
  const { vadThreshold } = storeToRefs(voice)
  const { model: llmModel, thinkingMode } = storeToRefs(llm)

  function reset(): void {
    connection.reset()
    appearance.reset()
    voice.reset()
    llm.reset()
  }

  return {
    serverUrl,
    token,
    language,
    themeMode,
    primaryHue,
    vadThreshold,
    llmModel,
    thinkingMode,
    reset,
  }
})

export { useAppearanceSettingsStore } from './settings/appearance'
export { useConnectionSettingsStore } from './settings/connection'
export { useLlmSettingsStore } from './settings/llm'
export {
  useVoiceSettingsStore,
  POSITIVE_THRESHOLD_DEFAULT,
  NEGATIVE_THRESHOLD_DEFAULT,
  REDEMPTION_FRAMES_DEFAULT,
  SILERO_FRAME_MS,
  STT_LANGUAGE_DEFAULT,
  SELECTED_DEVICE_DEFAULT,
  INPUT_MODE_DEFAULT,
  NOISE_SUPPRESSION_DEFAULT,
  ECHO_CANCELLATION_DEFAULT,
  AUTO_GAIN_CONTROL_DEFAULT,
  type SttLanguage,
  type VoiceInputMode,
} from './settings/voice'
