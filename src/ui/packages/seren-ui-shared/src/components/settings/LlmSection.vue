<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useConnectionSettingsStore } from '../../stores/settings/connection'
import { useLlmSettingsStore, type ThinkingMode } from '../../stores/settings/llm'

interface ModelInfo { id: string, description: string | null }
interface ProviderGroup { id: string, models: ModelInfo[] }

const store = useLlmSettingsStore()
const { provider, model, thinkingMode } = storeToRefs(store)
const connection = useConnectionSettingsStore()

const loading = ref(false)
const error = ref<string | null>(null)
const rawModels = ref<ModelInfo[]>([])

/**
 * Group the flat `/api/models` response by provider. OpenClaw returns
 * ids in the form `provider/model` (e.g. `ollama/seren-qwen`), so we
 * derive the provider by splitting on the first slash.
 */
const groups = computed<ProviderGroup[]>(() => {
  const byProvider = new Map<string, ModelInfo[]>()
  for (const m of rawModels.value) {
    const slash = m.id.indexOf('/')
    const prov = slash > 0 ? m.id.slice(0, slash) : 'other'
    if (!byProvider.has(prov)) byProvider.set(prov, [])
    byProvider.get(prov)!.push(m)
  }
  return Array.from(byProvider.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([id, models]) => ({ id, models }))
})

const selectedGroup = computed<ProviderGroup | undefined>(() =>
  groups.value.find(g => g.id === provider.value),
)

const thinkingModes: ThinkingMode[] = ['auto', 'off', 'low', 'medium', 'high']

function resolveBaseUrl(): string {
  // Prefer the user-set WS URL (rewritten to HTTP) so we hit the same
  // server they chat with. Fall back to same-origin — the nginx proxy in
  // front of seren-web forwards `/api/*` to seren-api.
  const raw = connection.serverUrl || ''
  if (!raw) return ''
  try {
    const u = new URL(raw.replace(/^ws/, 'http'))
    u.pathname = ''
    return u.origin
  }
  catch {
    return ''
  }
}

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    const base = resolveBaseUrl()
    const res = await fetch(`${base}/api/models`, {
      headers: connection.token ? { Authorization: `Bearer ${connection.token}` } : {},
    })
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    rawModels.value = await res.json() as ModelInfo[]
  }
  catch (e) {
    error.value = e instanceof Error ? e.message : 'Unknown error'
    rawModels.value = []
  }
  finally {
    loading.value = false
  }
}

function onProviderChange(): void {
  // When the provider changes, clear the model so we don't keep a stale
  // id like `openai/gpt-4o` selected under `ollama`.
  model.value = undefined
}

function modelLabel(m: ModelInfo): string {
  const slash = m.id.indexOf('/')
  const tail = slash > 0 ? m.id.slice(slash + 1) : m.id
  return m.description ? `${tail} — ${m.description}` : tail
}

onMounted(load)
</script>

<template>
  <section class="settings-section">
    <h3 class="settings-section__title">{{ $t('settings.llm.title') }}</h3>

    <div v-if="loading" class="settings-field">
      <p class="settings-field__hint">{{ $t('settings.llm.loading') }}</p>
    </div>

    <div v-else-if="error" class="settings-field">
      <p class="settings-field__hint" style="color: oklch(0.7 0.15 25);">
        {{ $t('settings.llm.loadError', { error }) }}
      </p>
      <button type="button" class="settings-section__btn" @click="load()">
        {{ $t('settings.llm.retry') }}
      </button>
    </div>

    <template v-else-if="groups.length">
      <div class="settings-field">
        <label class="settings-field__label" for="llm-provider">
          {{ $t('settings.llm.provider') }}
        </label>
        <select
          id="llm-provider"
          v-model="provider"
          class="settings-field__select"
          @change="onProviderChange"
        >
          <option :value="undefined">{{ $t('settings.llm.providerDefault') }}</option>
          <option v-for="g in groups" :key="g.id" :value="g.id">
            {{ g.id }}
          </option>
        </select>
      </div>

      <div v-if="selectedGroup" class="settings-field">
        <label class="settings-field__label" for="llm-model">
          {{ $t('settings.llm.model') }}
        </label>
        <select id="llm-model" v-model="model" class="settings-field__select">
          <option :value="undefined">{{ $t('settings.llm.modelDefault') }}</option>
          <option v-for="m in selectedGroup.models" :key="m.id" :value="m.id">
            {{ modelLabel(m) }}
          </option>
        </select>
      </div>
    </template>

    <div class="settings-field">
      <label class="settings-field__label" for="llm-model-custom">
        {{ $t('settings.llm.customModel') }}
      </label>
      <input
        id="llm-model-custom"
        v-model="model"
        type="text"
        placeholder="provider/model"
        class="settings-field__input"
      >
      <p class="settings-field__hint">{{ $t('settings.llm.customModelHint') }}</p>
    </div>

    <div class="settings-field">
      <label class="settings-field__label" for="llm-thinking">
        {{ $t('settings.llm.thinkingMode') }}
      </label>
      <select id="llm-thinking" v-model="thinkingMode" class="settings-field__select">
        <option v-for="t in thinkingModes" :key="t" :value="t">
          {{ $t(`settings.llm.thinking.${t}`) }}
        </option>
      </select>
      <p class="settings-field__hint">{{ $t('settings.llm.thinkingHint') }}</p>
    </div>

    <div class="settings-section__actions">
      <button type="button" class="settings-section__btn" @click="load()">
        {{ $t('settings.llm.refresh') }}
      </button>
      <button type="button" class="settings-section__btn" @click="store.reset()">
        {{ $t('settings.common.reset') }}
      </button>
    </div>
  </section>
</template>

<style scoped>
@import './section-common.css';
</style>
