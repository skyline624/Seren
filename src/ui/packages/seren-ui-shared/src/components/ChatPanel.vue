<script setup lang="ts">
import { ref, computed, nextTick, watch, onUnmounted } from 'vue'
import { useChatStore } from '../stores/chat'

const store = useChatStore()
const inputText = ref('')
const messagesContainer = ref<HTMLElement>()

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
  nextTick(scrollToBottom)
}

function handleKeydown(e: KeyboardEvent): void {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    handleSend()
  }
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

// Auto-scroll when new messages arrive
watch(() => store.messages.length, () => nextTick(scrollToBottom))
watch(() => store.currentAssistantContent, () => nextTick(scrollToBottom))
</script>

<template>
  <div class="chat-panel">
    <!-- Connection status banner -->
    <div v-if="!isConnected" :class="['chat-status', `chat-status--${store.connectionStatus}`]">
      {{ statusLabel }}
    </div>
    <div v-if="store.lastError" class="chat-status chat-status--error" @click="store.lastError = null">
      {{ store.lastError }}
    </div>
    <div ref="messagesContainer" class="chat-messages">
      <div
        v-for="msg in store.messages"
        :key="msg.id"
        :class="['chat-message', `chat-message--${msg.role}`]"
      >
        <span class="chat-message__role">{{ msg.role === 'user' ? 'You' : 'Seren' }}</span>
        <span class="chat-message__content">{{ msg.content }}</span>
      </div>
      <!-- Streaming assistant message -->
      <div v-if="store.isStreaming && store.currentAssistantContent" class="chat-message chat-message--assistant">
        <span class="chat-message__role">Seren</span>
        <span class="chat-message__content streaming">{{ store.currentAssistantContent }}&#9612;</span>
      </div>
    </div>
    <div v-if="micError" class="chat-mic-error">
      {{ micError }}
    </div>
    <div class="chat-input">
      <textarea
        v-model="inputText"
        :disabled="store.isStreaming"
        placeholder="Type a message..."
        rows="1"
        @keydown="handleKeydown"
      />
      <button
        v-if="isMicAvailable"
        :class="['chat-mic-btn', { 'chat-mic-btn--active': isMicActive, 'chat-mic-btn--listening': isListening }]"
        :title="isMicActive ? 'Stop microphone' : 'Start microphone'"
        @click="toggleMic"
      >
        <svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor">
          <path v-if="!isMicActive" d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm-1-9c0-.55.45-1 1-1s1 .45 1 1v6c0 .55-.45 1-1 1s-1-.45-1-1V5zm6 6c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z" />
          <path v-else d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5-3c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z" />
        </svg>
      </button>
      <button :disabled="!inputText.trim() || store.isStreaming" @click="handleSend">
        Send
      </button>
    </div>
  </div>
</template>

<style scoped>
.chat-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  max-height: 100vh;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  overflow: hidden;
  background: #fff;
}

.chat-status {
  padding: 0.375rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 500;
  text-align: center;
}

.chat-status--idle {
  background: #fef2f2;
  color: #dc2626;
}

.chat-status--connecting,
.chat-status--authenticating,
.chat-status--announcing {
  background: #fffbeb;
  color: #d97706;
}

.chat-status--error {
  background: #fef2f2;
  color: #dc2626;
  cursor: pointer;
}

.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.chat-message {
  padding: 0.5rem 0.75rem;
  border-radius: 6px;
  max-width: 80%;
  word-wrap: break-word;
}

.chat-message--user {
  align-self: flex-end;
  background: #3b82f6;
  color: #fff;
}

.chat-message--assistant {
  align-self: flex-start;
  background: #f1f5f9;
  color: #1e293b;
}

.chat-message__role {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  opacity: 0.7;
  display: block;
  margin-bottom: 0.25rem;
}

.chat-message--user .chat-message__role {
  color: #dbeafe;
}

.chat-message--assistant .chat-message__role {
  color: #64748b;
}

.streaming {
  font-style: italic;
}

.chat-mic-error {
  padding: 0.25rem 0.75rem;
  color: #ef4444;
  font-size: 0.75rem;
  background: #fef2f2;
}

.chat-input {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem;
  border-top: 1px solid #e2e8f0;
  background: #f8fafc;
  align-items: center;
}

.chat-input textarea {
  flex: 1;
  padding: 0.5rem;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  resize: none;
  font-family: inherit;
  font-size: 0.875rem;
}

.chat-input textarea:focus {
  outline: none;
  border-color: #3b82f6;
}

.chat-input button {
  padding: 0.5rem 1rem;
  background: #3b82f6;
  color: #fff;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.875rem;
  font-weight: 500;
}

.chat-input button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.chat-input button:not(:disabled):hover {
  background: #2563eb;
}

.chat-mic-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  padding: 0;
  border-radius: 50%;
  background: #e2e8f0;
  color: #475569;
  border: none;
  cursor: pointer;
  transition: background 0.15s, color 0.15s;
}

.chat-mic-btn:hover {
  background: #cbd5e1;
}

.chat-mic-btn--active {
  background: #ef4444;
  color: #fff;
}

.chat-mic-btn--active:hover {
  background: #dc2626;
}

.chat-mic-btn--listening {
  background: #f97316;
  color: #fff;
  animation: pulse 1.2s ease-in-out infinite;
}

@keyframes pulse {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.1); }
}
</style>
