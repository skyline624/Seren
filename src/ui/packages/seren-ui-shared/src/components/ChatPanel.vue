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
const isThinking = computed(() => store.isStreaming && !store.currentAssistantContent)

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
    <div v-if="store.lastError" class="chat-status chat-status--error" @click="store.lastError = null">
      {{ store.lastError }}
    </div>

    <!-- Messages area with gradient mask -->
    <div ref="messagesContainer" class="chat-messages">
      <div
        v-for="msg in store.messages"
        :key="msg.id"
        :class="['chat-bubble', `chat-bubble--${msg.role}`]"
      >
        <span class="chat-bubble__label">{{ msg.role === 'user' ? 'You' : 'Seren' }}</span>
        <p class="chat-bubble__text">{{ msg.content }}</p>
      </div>

      <!-- Thinking indicator -->
      <div v-if="isThinking" class="chat-bubble chat-bubble--assistant">
        <span class="chat-bubble__label">Seren</span>
        <span class="thinking-dots">
          <span />
          <span />
          <span />
        </span>
      </div>

      <!-- Streaming assistant message -->
      <div v-if="store.isStreaming && store.currentAssistantContent" class="chat-bubble chat-bubble--assistant">
        <span class="chat-bubble__label">Seren</span>
        <p class="chat-bubble__text">{{ store.currentAssistantContent }}</p>
      </div>
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
            v-if="hasInput"
            :disabled="store.isStreaming"
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
        title="Clear messages"
        @click="store.clearMessages()"
      >
        <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor">
          <path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z" />
        </svg>
      </button>
    </div>
  </div>
</template>

<style scoped>
.chat-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  border-radius: 16px;
  overflow: visible;
  background: rgba(10, 30, 40, 0.7);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border: 1px solid rgba(100, 180, 200, 0.2);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
  position: relative;
}

/* ── Scan bar ────────────────────────────────────────────────────── */
.scan-bar {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 2px;
  overflow: hidden;
  border-radius: 16px 16px 0 0;
  z-index: 5;
}

.scan-bar__indicator {
  width: 30%;
  height: 100%;
  background: rgba(13, 148, 136, 0.6);
  border-radius: 2px;
  animation: scan 2s linear infinite;
}

@keyframes scan {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(400%); }
}

/* ── Status banners ──────────────────────────────────────────────── */
.chat-status {
  padding: 0.375rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 500;
  text-align: center;
  flex-shrink: 0;
}

.chat-status--idle {
  background: rgba(239, 68, 68, 0.15);
  color: #fca5a5;
}

.chat-status--connecting,
.chat-status--authenticating,
.chat-status--announcing {
  background: rgba(217, 119, 6, 0.15);
  color: #fcd34d;
}

.chat-status--error {
  background: rgba(239, 68, 68, 0.15);
  color: #fca5a5;
  cursor: pointer;
}

/* ── Messages area ───────────────────────────────────────────────── */
.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 1.25rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.625rem;
  mask-image: linear-gradient(to bottom, transparent 0%, black 12%);
  -webkit-mask-image: linear-gradient(to bottom, transparent 0%, black 12%);
  scrollbar-width: thin;
  scrollbar-color: rgba(100, 180, 200, 0.2) transparent;
}

.chat-messages::-webkit-scrollbar {
  width: 3px;
}

.chat-messages::-webkit-scrollbar-thumb {
  background: rgba(100, 180, 200, 0.25);
  border-radius: 99px;
}

/* ── Message bubbles ─────────────────────────────────────────────── */
.chat-bubble {
  padding: 0.75rem 1rem;
  border-radius: 16px;
  max-width: 85%;
  word-wrap: break-word;
  line-height: 1.6;
  font-size: 0.875rem;
  min-width: 80px;
}

.chat-bubble--user {
  align-self: flex-end;
  margin-left: 3rem;
  background: rgba(38, 38, 38, 0.8);
  color: #e2e8f0;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.15);
}

.chat-bubble--assistant {
  align-self: flex-start;
  margin-right: 3rem;
  background: rgba(13, 80, 75, 0.5);
  color: #e2e8f0;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.1);
}

.chat-bubble__label {
  font-size: 0.65rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  display: block;
  margin-bottom: 0.25rem;
}

.chat-bubble--user .chat-bubble__label {
  color: rgba(255, 255, 255, 0.45);
}

.chat-bubble--assistant .chat-bubble__label {
  color: #14b8a6;
  opacity: 0.7;
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
  background: #14b8a6;
  animation: dotBounce 1.4s ease-in-out infinite;
}

.thinking-dots span:nth-child(2) {
  animation-delay: 0.15s;
}

.thinking-dots span:nth-child(3) {
  animation-delay: 0.3s;
}

@keyframes dotBounce {
  0%, 80%, 100% { opacity: 0.3; transform: scale(0.8); }
  40% { opacity: 1; transform: scale(1.1); }
}

/* ── Mic error ───────────────────────────────────────────────────── */
.chat-mic-error {
  padding: 0.25rem 0.75rem;
  color: #fca5a5;
  font-size: 0.75rem;
  background: rgba(239, 68, 68, 0.1);
  flex-shrink: 0;
}

/* ── Input area ──────────────────────────────────────────────────── */
.chat-input-area {
  display: flex;
  align-items: flex-end;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  background: rgba(13, 148, 136, 0.06);
  border-radius: 0 0 16px 16px;
}

.chat-input-area textarea {
  flex: 1;
  min-height: 40px;
  max-height: 200px;
  padding: 0.625rem 0.875rem;
  border: none;
  border-radius: 16px;
  resize: none;
  font-family: inherit;
  font-size: 0.875rem;
  background: rgba(15, 23, 42, 0.5);
  color: #e2e8f0;
  outline: none;
  transition: background 0.2s;
  line-height: 1.5;
}

.chat-input-area textarea::placeholder {
  color: #546a7b;
}

.chat-input-area textarea:focus {
  background: rgba(15, 23, 42, 0.7);
}

.chat-input-actions {
  display: flex;
  gap: 0.375rem;
  align-items: center;
  flex-shrink: 0;
}

/* ── Send button ─────────────────────────────────────────────────── */
.chat-send-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  padding: 0;
  background: rgba(230, 230, 230, 0.85);
  color: #1e293b;
  border: none;
  border-radius: 50%;
  cursor: pointer;
  transition: background 0.2s, transform 0.15s;
  flex-shrink: 0;
}

.chat-send-btn:hover {
  background: #fff;
  transform: scale(1.05);
}

.chat-send-btn:disabled {
  opacity: 0.3;
  cursor: not-allowed;
  transform: none;
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

/* ── Mic button ──────────────────────────────────────────────────── */
.chat-mic-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  padding: 0;
  border-radius: 50%;
  background: rgba(100, 180, 200, 0.12);
  color: #64748b;
  border: none;
  cursor: pointer;
  transition: background 0.15s, color 0.15s, transform 0.15s;
}

.chat-mic-btn:hover {
  background: rgba(100, 180, 200, 0.2);
  color: #94a3b8;
}

.chat-mic-btn--active {
  background: rgba(239, 68, 68, 0.25);
  color: #fca5a5;
}

.chat-mic-btn--active:hover {
  background: rgba(239, 68, 68, 0.35);
}

.chat-mic-btn--listening {
  background: rgba(249, 115, 22, 0.25);
  color: #fdba74;
  animation: pulse 1.2s ease-in-out infinite;
}

@keyframes pulse {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.12); }
}

/* ── Action buttons below chat ───────────────────────────────────── */
.chat-actions {
  position: absolute;
  bottom: -2.25rem;
  right: 0.5rem;
  display: flex;
  gap: 0.375rem;
}

.chat-action-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: none;
  border-radius: 8px;
  background: rgba(38, 38, 38, 0.7);
  color: #64748b;
  cursor: pointer;
  transition: color 0.2s, background 0.2s;
}

.chat-action-btn:hover {
  color: #ef4444;
  background: rgba(239, 68, 68, 0.15);
}
</style>
