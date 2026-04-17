import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

export interface SerenSettings {
  serverUrl: string
  token: string
  language: 'fr' | 'en'
  avatarMode: 'vrm' | 'live2d'
  vadThreshold: number
  /**
   * Optional LLM provider id (e.g. `"ollama"`, `"openai"`, `"anthropic"`).
   * Currently informational — the model id below already encodes the
   * provider via its `provider/model` prefix. Kept as a separate field
   * so a future Settings UI can show a Provider dropdown that filters
   * the Model dropdown options.
   */
  llmProvider?: string
  /**
   * Optional full model id sent to OpenClaw in the `model` field of
   * each chat completion request. When set, overrides the active
   * character's default `AgentId`. Example values:
   * `"ollama/qwen3.5:cloud"`, `"openai/gpt-4o-mini"`.
   */
  llmModel?: string
}

const STORAGE_KEY = 'seren-settings'

function loadFromStorage(): Partial<SerenSettings> {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? JSON.parse(raw) : {}
  }
  catch {
    return {}
  }
}

function saveToStorage(settings: SerenSettings): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings))
  }
  catch {
    // storage unavailable (SSR, private mode quota exceeded)
  }
}

export const useSettingsStore = defineStore('settings', () => {
  const stored = loadFromStorage()

  const serverUrl = ref(stored.serverUrl ?? '')
  const token = ref(stored.token ?? '')
  const language = ref<'fr' | 'en'>(stored.language ?? 'fr')
  const avatarMode = ref<'vrm' | 'live2d'>(stored.avatarMode ?? 'vrm')
  const vadThreshold = ref(stored.vadThreshold ?? 0.5)
  const llmProvider = ref<string | undefined>(stored.llmProvider)
  const llmModel = ref<string | undefined>(stored.llmModel)

  function save(): void {
    saveToStorage({
      serverUrl: serverUrl.value,
      token: token.value,
      language: language.value,
      avatarMode: avatarMode.value,
      vadThreshold: vadThreshold.value,
      llmProvider: llmProvider.value,
      llmModel: llmModel.value,
    })
  }

  function reset(): void {
    serverUrl.value = ''
    token.value = ''
    language.value = 'fr'
    avatarMode.value = 'vrm'
    vadThreshold.value = 0.5
    llmProvider.value = undefined
    llmModel.value = undefined
    save()
  }

  // Auto-persist on change
  watch(
    [serverUrl, token, language, avatarMode, vadThreshold, llmProvider, llmModel],
    save,
    { deep: true },
  )

  return {
    serverUrl,
    token,
    language,
    avatarMode,
    vadThreshold,
    llmProvider,
    llmModel,
    save,
    reset,
  }
})
