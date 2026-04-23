<script lang="ts">
/**
 * Map the server-side stream error `code` to a stable i18n key.
 * Falls back to `unknown` for codes we haven't wired up, so the
 * dialog always renders something sensible even on future taxonomy
 * extensions. Exported as a pure function so unit tests can exercise
 * every branch without mounting the component.
 */
export function resolveErrorKey(code?: string | null): string {
  const c = code ?? ''
  if (c === 'stream_idle_timeout' || c === 'idle_timeout') {
    return 'chat.error.codes.idle_timeout'
  }
  if (c === 'stream_total_timeout' || c === 'total_timeout') {
    return 'chat.error.codes.total_timeout'
  }
  if (c.includes('auth') || c === 'unauthorized' || c === '401') {
    return 'chat.error.codes.auth'
  }
  if (c.includes('not_found') || c === '404' || c === 'model_not_found') {
    return 'chat.error.codes.model_not_found'
  }
  return 'chat.error.codes.unknown'
}
</script>

<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { useChatStore } from '../stores/chat'

/**
 * Full-screen modal shown when the chat pipeline surfaces a permanent
 * error (model timeout, auth failure, unknown model, …). Replaces the
 * inline banner that used to live in `ChatPanel.vue` — an error on the
 * LLM side is a blocking situation and deserves a first-class dialog
 * with a clear explanation + actionable buttons.
 *
 * Mounted once at the root of the app (see `App.vue`). The parent
 * wires `@open-settings` so the "Change model" button can surface the
 * Settings drawer without this component knowing about the shell.
 */
const store = useChatStore()

const emit = defineEmits<{
  'open-settings': []
}>()

const visible = computed(() => store.lastError !== null)

const errorKey = computed<string>(() => resolveErrorKey(store.lastError?.code))

const canRetry = computed<boolean>(() =>
  store.lastError?.category === 'transient'
  && store.messages.some(m => m.role === 'user'),
)

function onDismiss(): void {
  store.lastError = null
}

function onRetry(): void {
  store.retryLastMessage()
}

function onChangeModel(): void {
  emit('open-settings')
  onDismiss()
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && visible.value) {
    onDismiss()
  }
}

onMounted(() => document.addEventListener('keydown', onKeydown))
onUnmounted(() => document.removeEventListener('keydown', onKeydown))
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" class="dialog-overlay" @click.self="onDismiss">
      <div class="dialog-panel" role="alertdialog" aria-modal="true">
        <header class="dialog-panel__header">
          <svg
            class="dialog-panel__icon"
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 24 24"
            width="32"
            height="32"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
            <line x1="12" y1="9" x2="12" y2="13" />
            <line x1="12" y1="17" x2="12.01" y2="17" />
          </svg>
          <h2 class="dialog-panel__title">{{ $t('chat.error.title') }}</h2>
        </header>

        <div class="dialog-panel__body">
          <p class="dialog-panel__message">{{ $t(errorKey) }}</p>
          <p v-if="store.lastError?.failedProvider" class="dialog-panel__provider">
            {{ $t('chat.error.provider', { provider: store.lastError.failedProvider }) }}
          </p>
          <details v-if="store.lastError?.message" class="dialog-panel__details">
            <summary>{{ $t('chat.error.details') }}</summary>
            <pre>{{ store.lastError.message }}<template v-if="store.lastError.code"> ({{ store.lastError.code }})</template></pre>
          </details>
        </div>

        <footer class="dialog-panel__actions">
          <button
            v-if="canRetry"
            type="button"
            class="dialog-panel__btn dialog-panel__btn--primary"
            @click="onRetry"
          >
            {{ $t('chat.error.actions.retry') }}
          </button>
          <button
            type="button"
            class="dialog-panel__btn"
            @click="onChangeModel"
          >
            {{ $t('chat.error.actions.changeModel') }}
          </button>
          <button
            type="button"
            class="dialog-panel__btn dialog-panel__btn--dismiss"
            @click="onDismiss"
          >
            {{ $t('chat.error.actions.dismiss') }}
          </button>
        </footer>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.dialog-overlay {
  position: fixed;
  inset: 0;
  z-index: 200;
  background: rgba(0, 0, 0, 0.55);
  backdrop-filter: blur(6px);
  display: flex;
  align-items: center;
  justify-content: center;
  animation: fadeIn 0.15s ease-out;
}

.dialog-panel {
  width: 480px;
  max-width: 92vw;
  max-height: 85vh;
  background: rgba(10, 30, 40, 0.94);
  backdrop-filter: blur(20px);
  border: 1px solid oklch(0.55 0.15 25 / 0.35);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  animation: popIn 0.2s ease-out;
  overflow-y: auto;
}

.dialog-panel__header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.dialog-panel__icon {
  color: oklch(0.72 0.17 25);
  flex-shrink: 0;
}

.dialog-panel__title {
  font-size: 1.125rem;
  font-weight: 600;
  color: #f1f5f9;
  margin: 0;
}

.dialog-panel__body {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  color: #e2e8f0;
}

.dialog-panel__message {
  margin: 0;
  line-height: 1.5;
}

.dialog-panel__provider {
  margin: 0;
  padding: 0.4rem 0.6rem;
  background: rgba(239, 68, 68, 0.08);
  border-left: 2px solid oklch(0.55 0.15 25 / 0.6);
  border-radius: 4px;
  font-size: 0.85rem;
  color: #cbd5e1;
  font-family: ui-monospace, monospace;
}

.dialog-panel__details {
  font-size: 0.8rem;
  color: #94a3b8;
}

.dialog-panel__details summary {
  cursor: pointer;
  user-select: none;
  padding: 0.25rem 0;
}

.dialog-panel__details pre {
  margin: 0.35rem 0 0;
  padding: 0.5rem;
  background: rgba(0, 0, 0, 0.3);
  border-radius: 4px;
  font-size: 0.75rem;
  white-space: pre-wrap;
  word-break: break-word;
}

.dialog-panel__actions {
  display: flex;
  gap: 0.5rem;
  justify-content: flex-end;
  flex-wrap: wrap;
}

.dialog-panel__btn {
  padding: 0.5rem 1rem;
  border: 1px solid rgba(100, 180, 200, 0.25);
  background: rgba(100, 180, 200, 0.1);
  color: #e2e8f0;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.875rem;
  transition: background 0.15s, border-color 0.15s;
}

.dialog-panel__btn:hover {
  background: rgba(100, 180, 200, 0.2);
  border-color: rgba(100, 180, 200, 0.4);
}

.dialog-panel__btn--primary {
  background: oklch(0.72 0.17 200 / 0.3);
  border-color: oklch(0.72 0.17 200 / 0.5);
}

.dialog-panel__btn--primary:hover {
  background: oklch(0.72 0.17 200 / 0.45);
}

.dialog-panel__btn--dismiss {
  color: #94a3b8;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

@keyframes popIn {
  from { opacity: 0; transform: scale(0.95); }
  to { opacity: 1; transform: scale(1); }
}
</style>
