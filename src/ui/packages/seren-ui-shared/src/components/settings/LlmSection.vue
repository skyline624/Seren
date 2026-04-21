<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useConnectionSettingsStore } from '../../stores/settings/connection'
import { useLlmSettingsStore, type ThinkingMode } from '../../stores/settings/llm'

interface ModelInfo { id: string, description: string | null }

const store = useLlmSettingsStore()
const { model, thinkingMode } = storeToRefs(store)
const connection = useConnectionSettingsStore()

const loading = ref(false)
const applying = ref(false)
const applyStatus = ref<'idle' | 'ok' | 'error'>('idle')
const error = ref<string | null>(null)
const rawModels = ref<ModelInfo[]>([])

// Remember the last-applied value so the Apply button can grey itself
// out when the current dropdown selection already matches what OpenClaw
// is running. Starts undefined — the first Apply sets it.
const appliedModel = ref<string | undefined>(undefined)
const applyDisabled = computed(() =>
  loading.value || applying.value || model.value === appliedModel.value,
)

// Defensive alphabetical sort — the endpoint already orders by id, but
// this guarantees deterministic UI across server versions.
const orderedModels = computed<ModelInfo[]>(() =>
  [...rawModels.value].sort((a, b) => a.id.localeCompare(b.id)),
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

function authHeaders(): Record<string, string> {
  return connection.token ? { Authorization: `Bearer ${connection.token}` } : {}
}

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    const base = resolveBaseUrl()
    const res = await fetch(`${base}/api/models`, { headers: authHeaders() })
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

/**
 * Ask OpenClaw (via Seren's POST /api/models/refresh) to rescan its
 * provider catalogs, then poll /api/models until the fresh list comes
 * back. Needed after `ollama pull` — OpenClaw caches the Ollama catalog
 * at boot and new tags aren't visible otherwise.
 */
async function refresh(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    const base = resolveBaseUrl()
    const res = await fetch(`${base}/api/models/refresh`, {
      method: 'POST',
      headers: authHeaders(),
    })
    if (!res.ok && res.status !== 202) throw new Error(`HTTP ${res.status}`)

    // Poll until the gateway finishes its post-restart handshake AND
    // rebuilds the Pi SDK catalog. First successful hit (non-empty list)
    // stops the loop; otherwise we bail after ~45 s with an error.
    const deadline = Date.now() + 45_000
    let attempt = 0
    while (Date.now() < deadline) {
      attempt++
      // Back off a little early on — the first 2 polls usually see the
      // gateway still starting; too aggressive and we just burn requests.
      await new Promise(resolve => setTimeout(resolve, attempt === 1 ? 4000 : 3000))
      const probe = await fetch(`${base}/api/models`, { headers: authHeaders() })
      if (!probe.ok) continue
      const next = await probe.json() as ModelInfo[]
      if (next.length > 0) {
        rawModels.value = next
        loading.value = false
        return
      }
    }
    throw new Error('refresh timeout')
  }
  catch (e) {
    error.value = e instanceof Error ? e.message : 'Unknown error'
    rawModels.value = []
  }
  finally {
    loading.value = false
  }
}

/**
 * Ask Seren to pin the current dropdown selection as OpenClaw's default
 * model, then trigger a gateway restart. The server writes the value
 * into `agents.defaults.model.primary` of `openclaw.json` atomically
 * and signals SIGUSR1 — the new model becomes active on the next chat
 * turn (no admin scope needed client-side).
 */
async function applyModel(): Promise<void> {
  applying.value = true
  applyStatus.value = 'idle'
  error.value = null
  try {
    const base = resolveBaseUrl()
    const res = await fetch(`${base}/api/models/apply`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({ model: model.value ?? null }),
    })
    if (!res.ok && res.status !== 202) throw new Error(`HTTP ${res.status}`)

    // Gateway restart takes ~5-8 s before the catalog is queryable again.
    // Poll /api/models until the list comes back, then consider the new
    // pin effective. 45 s cap mirrors the refresh flow.
    const deadline = Date.now() + 45_000
    let attempt = 0
    while (Date.now() < deadline) {
      attempt++
      await new Promise(resolve => setTimeout(resolve, attempt === 1 ? 4000 : 3000))
      const probe = await fetch(`${base}/api/models`, { headers: authHeaders() })
      if (!probe.ok) continue
      const next = await probe.json() as ModelInfo[]
      if (next.length > 0) {
        rawModels.value = next
        appliedModel.value = model.value
        applyStatus.value = 'ok'
        applying.value = false
        return
      }
    }
    throw new Error('apply timeout')
  }
  catch (e) {
    error.value = e instanceof Error ? e.message : 'Unknown error'
    applyStatus.value = 'error'
  }
  finally {
    applying.value = false
  }
}

function modelLabel(m: ModelInfo): string {
  return m.description ? `${m.id} — ${m.description}` : m.id
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

    <div v-else-if="orderedModels.length" class="settings-field">
      <label class="settings-field__label" for="llm-model">
        {{ $t('settings.llm.model') }}
      </label>
      <select id="llm-model" v-model="model" class="settings-field__select">
        <option :value="undefined">{{ $t('settings.llm.modelDefault') }}</option>
        <option v-for="m in orderedModels" :key="m.id" :value="m.id">
          {{ modelLabel(m) }}
        </option>
      </select>
      <div class="settings-field__apply-row">
        <button
          type="button"
          class="settings-section__btn settings-section__btn--primary"
          :disabled="applyDisabled"
          @click="applyModel()"
        >
          {{ applying ? $t('settings.llm.applying') : $t('settings.llm.apply') }}
        </button>
        <span v-if="applyStatus === 'ok'" class="settings-field__apply-status settings-field__apply-status--ok">
          {{ $t('settings.llm.applyOk') }}
        </span>
      </div>
      <p class="settings-field__hint">{{ $t('settings.llm.applyHint') }}</p>
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
      <button type="button" class="settings-section__btn" :disabled="loading" @click="refresh()">
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

.settings-field__apply-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 0.5rem;
}

.settings-field__apply-status {
  font-size: 0.85rem;
}

.settings-field__apply-status--ok {
  color: oklch(0.78 0.12 150);
}

.settings-section__btn--primary {
  background: oklch(0.55 0.16 200 / 0.35);
  border-color: oklch(0.65 0.16 200 / 0.45);
}

.settings-section__btn--primary:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}
</style>
