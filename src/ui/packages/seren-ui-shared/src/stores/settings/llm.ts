import { defineStore } from 'pinia'
import { usePersistedRef } from '../../composables/usePersistedRef'

export type ThinkingMode = 'auto' | 'off' | 'low' | 'medium' | 'high'

/**
 * LLM routing settings. `model` is a fully-qualified `provider/model`
 * key served by OpenClaw's `/api/models`; the server forwards it to
 * OpenClaw per-request as an agent override when set.
 *
 * `thinkingMode` is stored but not yet forwarded to OpenClaw — the
 * plumbing through `SendTextMessageCommand` will follow in a later bloc.
 * Setting it today has no runtime effect; the UI surfaces it so users
 * can express an intent that gets wired as soon as the handler reads it.
 */
export const useLlmSettingsStore = defineStore('settings/llm', () => {
  const model = usePersistedRef<string | undefined>('seren/llm/model', undefined)
  const thinkingMode = usePersistedRef<ThinkingMode>('seren/llm/thinkingMode', 'auto')

  function reset(): void {
    model.value = undefined
    thinkingMode.value = 'auto'
  }

  return { model, thinkingMode, reset }
})
