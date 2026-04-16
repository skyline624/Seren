import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

export interface SerenSettings {
  serverUrl: string
  token: string
  language: 'fr' | 'en'
  avatarMode: 'vrm' | 'live2d'
  vadThreshold: number
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

  function save(): void {
    saveToStorage({
      serverUrl: serverUrl.value,
      token: token.value,
      language: language.value,
      avatarMode: avatarMode.value,
      vadThreshold: vadThreshold.value,
    })
  }

  function reset(): void {
    serverUrl.value = ''
    token.value = ''
    language.value = 'fr'
    avatarMode.value = 'vrm'
    vadThreshold.value = 0.5
    save()
  }

  // Auto-persist on change
  watch([serverUrl, token, language, avatarMode, vadThreshold], save, { deep: true })

  return {
    serverUrl,
    token,
    language,
    avatarMode,
    vadThreshold,
    save,
    reset,
  }
})
