<script setup lang="ts">
import { ref, computed, nextTick, watch, onUnmounted } from 'vue'
import { useChatStore } from '../stores/chat'

const store = useChatStore()
const inputText = ref('')
const messagesContainer = ref<HTMLElement>()
const textareaRef = ref<HTMLTextAreaElement>()

// ── Voice input (dynamic import — @seren/ui-audio is an optional peer dep) ──
const isMicAvailable = ref(false)
const isMicActive = ref(false)
const isListening = ref(false)
const micError = ref<string | null>(null)

// eslint-disable-next-line ts/consistent-type-definitions
type VoiceInputApi = {
  start: () => Promise<void>
  stop: () => void
}

let voiceInputModule: { useVoiceInput: (opts: any) => VoiceInputApi } | null = null
let vadStop: (() => void) | null = null

async function loadVoiceInput(): Promise<void> {
  try {
    voiceInputModule = await import('@seren/ui-audio' as string)
    isMicAvailable.value = true
  }
  catch {
    // @seren/ui-audio not installed — mic button stays hidden
  }
}
loadVoiceInput()

async function toggleMic(): Promise<void> {
  if (!voiceInputModule) return

  if (isMicActive.value) {
    vadStop?.()
    isMicActive.value = false
    isListening.value = false
    return
  }

  micError.value = null
  const vad = voiceInputModule.useVoiceInput({
    threshold: 0.5,
    onSpeechStart: () => {
      isListening.value = true
    },
    onSpeechEnd: (audio: Float32Array) => {
      isListening.value = false
      store.sendVoiceInput(audio)
    },
  })

  vadStop = vad.stop

  try {
    await vad.start()
    isMicActive.value = true
  }
  catch (e) {
    micError.value = e instanceof Error ? e.message : 'Microphone access denied'
    isMicActive.value = false
  }
}

onUnmounted(() => {
  vadStop?.()
})

// ── Text input ──────────────────────────────────────────────────────────────
function handleSend(): void {
  const text = inputText.value.trim()
  if (!text || store.isStreaming) return
  store.sendMessage(text)
  inputText.value = ''
  resetTextareaHeight()
  nextTick(scrollToBottom)
}

function handleKeydown(e: KeyboardEvent): void {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    handleSend()
  }
}

function autoResizeTextarea(): void {
  const el = textareaRef.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = `${Math.min(el.scrollHeight, 200)}px`
}

function resetTextareaHeight(): void {
  const el = textareaRef.value
  if (!el) return
  el.style.height = 'auto'
}

function scrollToBottom(): void {
  if (messagesContainer.value) {
    messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
  }
}

// ── History pagination on scroll-back ───────────────────────────────────────
// When the user scrolls within ~80 px of the top of the messages list and
// older messages exist server-side, ask for another page. We snapshot the
// scroll height before mutating so we can keep the visible position stable
// after the new items are prepended.
function handleMessagesScroll(): void {
  const el = messagesContainer.value
  if (!el) return
  if (el.scrollTop > 80) return
  if (!store.historyHasMore || store.historyLoading) return

  const heightBefore = el.scrollHeight
  store.loadMoreHistory()
  const stop = watch(() => store.messages.length, () => {
    nextTick(() => {
      if (!messagesContainer.value) return
      const delta = messagesContainer.value.scrollHeight - heightBefore
      messagesContainer.value.scrollTop += delta
    })
    stop()
  })
}

function handleResetConversation(): void {
  if (!window.confirm('Réinitialiser la conversation ? Le contexte LLM sera vidé. La mémoire long-terme et les accès restent intacts.')) {
    return
  }
  store.resetConversation()
}

function handleAbortStream(): void {
  store.abortStream()
}

const degradationLabel = computed(() => {
  const d = store.degradationNotice
  if (!d) return ''
  // Prefer model basename for display (strip "provider/") — UI isn't meant
  // to advertise provider plumbing, just the user-facing model label.
  const shortFrom = d.from.split('/').pop() ?? d.from
  const shortTo = d.to.split('/').pop() ?? d.to
  if (d.from === d.to) {
    return `Nouvelle tentative sur ${shortTo}…`
  }
  return `${shortFrom} indisponible, bascule sur ${shortTo}…`
})

// ── Connection status ───────────────────────────────────────────────────────
const isConnected = computed(() => store.connectionStatus === 'ready')
const statusLabel = computed(() => {
  switch (store.connectionStatus) {
    case 'ready': return 'Connected'
    case 'connecting': return 'Connecting...'
    case 'authenticating': return 'Authenticating...'
    case 'announcing': return 'Announcing...'
    case 'idle': return 'Disconnected'
    default: return 'Disconnected'
  }
})

const hasInput = computed(() => inputText.value.trim().length > 0)
// Show the animated dots bubble when the backend flags the chain-of-thought
// phase OR while we are still waiting for the first token of the answer.
const isThinking = computed(() =>
  store.isThinking || (store.isStreaming && !store.currentAssistantContent),
)

// Auto-scroll when new messages arrive
watch(() => store.messages.length, () => nextTick(scrollToBottom))
watch(() => store.currentAssistantContent, () => nextTick(scrollToBottom))
</script>

<template>
  <div class="chat-panel">
    <!-- Scanning bar — visible during streaming -->
    <div v-if="store.isStreaming" class="scan-bar">
      <div class="scan-bar__indicator" />
    </div>

    <!-- Connection status banner -->
    <div v-if="!isConnected" :class="['chat-status', `chat-status--${store.connectionStatus}`]">
      {{ statusLabel }}
    </div>
    <div
      v-if="store.degradationNotice"
      class="chat-status chat-status--info"
    >
      {{ degradationLabel }}
    </div>
    <!-- Error feedback lives in `ChatErrorDialog` (mounted globally in
         App.vue) — it's a blocking popup with explanation + actions,
         richer than the tiny inline banner that used to live here. -->


    <!-- Messages area with gradient mask -->
    <div ref="messagesContainer" class="chat-messages" @scroll="handleMessagesScroll">
      <!-- Loader spinner pendant la pagination scroll-back -->
      <div v-if="store.historyLoading && store.messages.length > 0" class="chat-history-loader">
        <span class="thinking-dots"><span /><span /><span /></span>
      </div>
      <div
        v-for="msg in store.messages"
        :key="msg.id"
        :class="['chat-bubble', `chat-bubble--${msg.role}`]"
      >
        <span class="chat-bubble__label">{{ msg.role === 'user' ? 'You' : 'Seren' }}</span>
        <p class="chat-bubble__text">{{ msg.content }}</p>
      </div>

      <!-- Thinking indicator — bulle vide avec label pendant que Seren
           attend le premier token OU pense dans un canal analysis. -->
      <div v-if="isThinking && !store.currentAssistantContent" class="chat-bubble chat-bubble--assistant">
        <span class="chat-bubble__label">Seren</span>
      </div>

      <!-- Streaming assistant message -->
      <div
        v-if="store.isStreaming && store.currentAssistantContent"
        class="chat-bubble chat-bubble--assistant"
      >
        <span class="chat-bubble__label">Seren</span>
        <p class="chat-bubble__text">{{ store.currentAssistantContent }}</p>
      </div>

      <!-- Trailing dots : uniques pour tout le cycle de génération (thinking
           initial, chain-of-thought serveur, streaming du texte final).
           Toujours sous la dernière bulle Seren quelle que soit la phase. -->
      <span
        v-if="store.isStreaming || store.isThinking"
        class="thinking-dots thinking-dots--trailing"
        aria-label="Seren est en train de répondre"
      >
        <span />
        <span />
        <span />
      </span>
    </div>

    <!-- Mic error -->
    <div v-if="micError" class="chat-mic-error">
      {{ micError }}
    </div>

    <!-- Input area -->
    <div class="chat-input-area">
      <textarea
        ref="textareaRef"
        v-model="inputText"
        :disabled="store.isStreaming"
        placeholder="Write something..."
        rows="1"
        @keydown="handleKeydown"
        @input="autoResizeTextarea"
      />
      <div class="chat-input-actions">
        <button
          v-if="isMicAvailable"
          :class="['chat-mic-btn', { 'chat-mic-btn--active': isMicActive, 'chat-mic-btn--listening': isListening }]"
          :title="isMicActive ? 'Stop microphone' : 'Start microphone'"
          @click="toggleMic"
        >
          <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor">
            <path v-if="!isMicActive" d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm-1-9c0-.55.45-1 1-1s1 .45 1 1v6c0 .55-.45 1-1 1s-1-.45-1-1V5zm6 6c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z" />
            <path v-else d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5-3c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z" />
          </svg>
        </button>
        <Transition name="fade">
          <button
            v-if="store.isStreaming"
            class="chat-send-btn chat-send-btn--stop"
            title="Stop generation"
            @click="handleAbortStream"
          >
            <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor">
              <rect x="6" y="6" width="12" height="12" rx="1.5" />
            </svg>
          </button>
          <button
            v-else-if="hasInput"
            class="chat-send-btn"
            @click="handleSend"
          >
            <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor">
              <path d="M4 12l1.41 1.41L11 7.83V20h2V7.83l5.58 5.59L20 12l-8-8-8 8z" />
            </svg>
          </button>
        </Transition>
      </div>
    </div>

    <!-- Action buttons below chat -->
    <div class="chat-actions">
      <button
        class="chat-action-btn"
        title="Nouvelle conversation (vide le contexte LLM, garde la mémoire long-terme)"
        @click="handleResetConversation"
      >
        <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor">
          <path d="M17.65 6.35A7.958 7.958 0 0012 4a8 8 0 100 16 7.96 7.96 0 007.74-6h-2.08A6 6 0 1112 6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z" />
        </svg>
      </button>
    </div>
  </div>
</template>

<style scoped>
/* ════════════════════════════════════════════════════════════════════
 * AIRI-inspired chat panel
 * Glassmorphism on a translucent teal base, rounded 12px, blur 12px.
 * Reference: https://airi.moeru.ai/
 * ════════════════════════════════════════════════════════════════════ */

.chat-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  position: relative;

  border-radius: 12px;
  background: var(--airi-surface);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.1);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.25);

  font-family: 'Nunito Variable', 'Nunito', 'DM Sans', ui-sans-serif, system-ui, sans-serif;
  color: var(--airi-text);
  overflow: visible;
}

/* ── Scan bar (streaming indicator) ──────────────────────────────── */
.scan-bar {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 2px;
  overflow: hidden;
  border-radius: 12px 12px 0 0;
  z-index: 5;
}

.scan-bar__indicator {
  width: 30%;
  height: 100%;
  background: var(--airi-accent);
  opacity: 0.7;
  border-radius: 2px;
  animation: scan 2s linear infinite;
}

@keyframes scan {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(400%); }
}

/* ── Status banners ──────────────────────────────────────────────── */
.chat-status {
  padding: 0.4rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 500;
  text-align: center;
  flex-shrink: 0;
  border-radius: 8px;
  margin: 0.5rem 0.5rem 0;
}

.chat-status--idle {
  background: oklch(0.55 0.15 25 / 0.18);
  color: oklch(0.83 0.1 25);
}

/* Informational banner for transparent retries / fallback. Distinct from
 * error (which now lives in ChatErrorDialog) — warmer yellow tone,
 * matches the "thinking" palette. */
.chat-status--info {
  background: oklch(0.7 0.12 70 / 0.18);
  color: oklch(0.85 0.1 70);
}

.chat-status--connecting,
.chat-status--authenticating,
.chat-status--announcing {
  background: oklch(0.7 0.12 70 / 0.18);
  color: oklch(0.85 0.1 70);
}

/* ── Messages area ───────────────────────────────────────────────── */
.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 1rem 0.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  scrollbar-width: thin;
  scrollbar-color: oklch(0.74 0.127 var(--seren-hue) / 0.25) transparent;
}

.chat-messages::-webkit-scrollbar {
  width: 4px;
}

.chat-messages::-webkit-scrollbar-thumb {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.3);
  border-radius: 99px;
}

/* ── Loader pendant le chargement de l'historique paginé ─────────── */
.chat-history-loader {
  align-self: center;
  padding: 0.25rem 0;
}

/* ── Message bubbles ─────────────────────────────────────────────── */
.chat-bubble {
  padding: 0.6rem 0.9rem;
  border-radius: 14px;
  max-width: 85%;
  word-wrap: break-word;
  line-height: 1.55;
  font-size: 0.9rem;
}

.chat-bubble--user {
  align-self: flex-end;
  background: oklch(0.74 0.127 var(--seren-hue) / 0.28);
  color: var(--airi-text);
  border-bottom-right-radius: 4px;
}

.chat-bubble--assistant {
  align-self: flex-start;
  background: oklch(0.22 0.04 var(--seren-hue) / 0.6);
  color: var(--airi-text);
  border: 1px solid oklch(0.74 0.127 var(--seren-hue) / 0.08);
  border-bottom-left-radius: 4px;
}

.chat-bubble__label {
  font-size: 0.65rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  display: block;
  margin-bottom: 0.25rem;
  color: var(--airi-text-muted);
}

.chat-bubble--user .chat-bubble__label {
  color: oklch(0.85 0.08 var(--seren-hue));
}

.chat-bubble--assistant .chat-bubble__label {
  color: var(--airi-accent);
}

.chat-bubble__text {
  margin: 0;
  white-space: pre-wrap;
}

/* ── Thinking dots ───────────────────────────────────────────────── */
.thinking-dots {
  display: inline-flex;
  gap: 4px;
  padding: 4px 0;
}

.thinking-dots span {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--airi-accent);
  animation: dotBounce 1.4s ease-in-out infinite;
}

.thinking-dots span:nth-child(2) { animation-delay: 0.15s; }
.thinking-dots span:nth-child(3) { animation-delay: 0.3s; }

/* Variante « sous la bulle » pendant le streaming. Alignée à gauche comme
   les bulles assistant + léger retrait horizontal pour tomber sous le texte,
   pas contre le bord. */
.thinking-dots--trailing {
  align-self: flex-start;
  padding: 2px 10px 2px 14px;
  margin-top: -0.25rem;
  margin-left: 6px;
}
.thinking-dots--trailing span {
  width: 5px;
  height: 5px;
  opacity: 0.7;
}

@keyframes dotBounce {
  0%, 80%, 100% { opacity: 0.3; transform: scale(0.8); }
  40% { opacity: 1; transform: scale(1.15); }
}

/* ── Mic error ───────────────────────────────────────────────────── */
.chat-mic-error {
  margin: 0 0.5rem;
  padding: 0.35rem 0.75rem;
  color: oklch(0.83 0.1 25);
  font-size: 0.75rem;
  background: oklch(0.55 0.15 25 / 0.15);
  border-radius: 6px;
  flex-shrink: 0;
}

/* ── Input area (rounded-top panel, AIRI pattern) ────────────────── */
.chat-input-area {
  position: relative;
  background: var(--airi-input-tint);
  border-radius: 12px 12px 0 0;
  padding: 0.75rem 0.75rem 0.5rem;
}

.chat-input-area textarea {
  width: 100%;
  min-height: 40px;
  max-height: 180px;
  padding: 0.5rem 0.75rem;
  border: none;
  border-radius: 8px;
  resize: none;
  font-family: inherit;
  font-size: 0.9rem;
  background: transparent;
  color: var(--airi-text);
  outline: none;
  line-height: 1.5;
  box-sizing: border-box;
}

.chat-input-area textarea::placeholder {
  color: var(--airi-text-muted);
  opacity: 0.7;
}

.chat-input-area textarea:focus { background: oklch(0 0 0 / 0.15); }

.chat-input-actions {
  display: flex;
  gap: 0.25rem;
  align-items: center;
  justify-content: flex-end;
  margin-top: 0.25rem;
}

/* ── AIRI-style square ghost buttons (32×32, rounded 6px) ────────── */
.chat-send-btn,
.chat-mic-btn,
.chat-action-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 0;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--airi-text-muted);
  cursor: pointer;
  transition: background 0.2s ease, color 0.2s ease, transform 0.15s ease;
}

.chat-send-btn:hover,
.chat-mic-btn:hover,
.chat-action-btn:hover {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.18);
  color: var(--airi-text);
}

.chat-send-btn:active,
.chat-mic-btn:active,
.chat-action-btn:active {
  transform: scale(0.94);
}

.chat-send-btn {
  background: var(--airi-accent);
  color: oklch(0.12 0.02 var(--seren-hue));
}

.chat-send-btn:hover {
  background: oklch(0.78 0.13 var(--seren-hue));
  color: oklch(0.12 0.02 var(--seren-hue));
}

.chat-send-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
  transform: none;
}

/* Stop variant: muted reddish tint to read as a destructive interrupt
 * without screaming — matches the AIRI ghost-button visual rhythm. */
.chat-send-btn--stop {
  background: oklch(0.55 0.15 25 / 0.22);
  color: oklch(0.88 0.1 25);
}

.chat-send-btn--stop:hover {
  background: oklch(0.55 0.15 25 / 0.35);
  color: oklch(0.93 0.1 25);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s, transform 0.2s;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
  transform: scale(0.7);
}

.chat-mic-btn--active {
  background: oklch(0.55 0.15 25 / 0.25);
  color: oklch(0.83 0.1 25);
}

.chat-mic-btn--active:hover {
  background: oklch(0.55 0.15 25 / 0.35);
  color: oklch(0.9 0.1 25);
}

.chat-mic-btn--listening {
  background: oklch(0.7 0.14 70 / 0.3);
  color: oklch(0.88 0.12 70);
  animation: pulse 1.2s ease-in-out infinite;
}

@keyframes pulse {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.1); }
}

/* ── Action buttons below chat ───────────────────────────────────── */
.chat-actions {
  position: absolute;
  bottom: -2.25rem;
  right: 0.5rem;
  display: flex;
  gap: 0.375rem;
}

.chat-actions .chat-action-btn {
  background: var(--airi-surface-strong);
  backdrop-filter: blur(12px);
  width: 28px;
  height: 28px;
}

.chat-actions .chat-action-btn:hover {
  background: oklch(0.55 0.15 25 / 0.2);
  color: oklch(0.83 0.1 25);
}
</style>
