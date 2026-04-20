import { defineStore } from 'pinia'
import { usePersistedRef } from '../../composables/usePersistedRef'

export type ThinkingMode = 'auto' | 'off' | 'low' | 'medium' | 'high'

/**
 * LLM routing settings. `provider` is informational (the provider id
 * is already encoded in the `model` prefix, e.g. `ollama/seren-qwen`);
 * it's kept to let the UI filter the model dropdown by provider.
 *
 * `thinkingMode` is stored but **not yet forwarded** to OpenClaw — the
 * plumbing through `SendTextMessageCommand` will follow in a later bloc.
 * Setting it today has no runtime effect; the UI surfaces it so users
 * can express an intent that gets wired as soon as the handler reads it.
 */
export const useLlmSettingsStore = defineStore('settings/llm', () => {
  const provider = usePersistedRef<string | undefined>('seren/llm/provider', undefined)
  const model = usePersistedRef<string | undefined>('seren/llm/model', undefined)
  const thinkingMode = usePersistedRef<ThinkingMode>('seren/llm/thinkingMode', 'auto')

  function reset(): void {
    provider.value = undefined
    model.value = undefined
    thinkingMode.value = 'auto'
  }

  return { provider, model, thinkingMode, reset }
})
