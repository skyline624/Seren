import { defineStore } from 'pinia'
import { usePersistedRef } from '../../composables/usePersistedRef'

/**
 * Voice input pipeline settings. Starts with just the VAD threshold
 * consumed by `ChatPanel.toggleMic()`; TTS/STT provider selection
 * will land here once their infrastructure is wired on the server.
 */
export const useVoiceSettingsStore = defineStore('settings/voice', () => {
  const vadThreshold = usePersistedRef<number>('seren/voice/vadThreshold', 0.5)

  function reset(): void {
    vadThreshold.value = 0.5
  }

  return { vadThreshold, reset }
})
