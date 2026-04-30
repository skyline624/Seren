<script setup lang="ts">
import { ref, computed, nextTick, watch, onMounted, onUnmounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useChatStore } from '../stores/chat'
import { useVoiceSettingsStore } from '../stores/settings/voice'
import ChatAttachmentChip from './ChatAttachmentChip.vue'
import {
  extractDropFiles,
  extractPasteFiles,
  pickFilesFromDialog,
} from '../composables/useAttachmentPicker'

const store = useChatStore()
const voiceSettings = useVoiceSettingsStore()
const {
  vadThreshold,
  negativeSpeechThreshold,
  redemptionFrames,
  sttLanguage,
  selectedDeviceId,
  inputMode,
  noiseSuppression,
  echoCancellation,
  autoGainControl,
} = storeToRefs(voiceSettings)
const inputText = ref('')
const messagesContainer = ref<HTMLElement>()
const textareaRef = ref<HTMLTextAreaElement>()

// ── Voice input (dynamic import — @seren/ui-audio is an optional peer dep) ──
const isMicAvailable = ref(false)
const isMicActive = ref(false)
const isListening = ref(false)
const micError = ref<string | null>(null)

// ── Voice dictation (transcribe-to-text-input flow) ─────────────────────────
// Independent VAD instance from the chat-mic above so the user can keep
// the chat-mic disabled while using "click to dictate into the textarea".
const isDictateActive = ref(false)
const isDictateListening = ref(false)
const isDictateBusy = ref(false)
let dictateVadStop: (() => void) | null = null

// Preferred STT engine is supplied by an optional voice module (e.g.
// `@seren/module-voxmind`) via a runtime registry. Static import kept
// minimal so this component compiles even when no voice module is
// installed (lite UI deployments) — the registry just returns undefined
// and the server falls back to its configured default engine.
import { getPreferredVoiceEngine } from '../composables/voiceEnginePreference'

// eslint-disable-next-line ts/consistent-type-definitions
type VoiceInputApi = {
  start: () => Promise<void>
  stop: () => void
  press?: () => void
  release?: () => void
}

let voiceInputModule: { useVoiceInput: (opts: any) => VoiceInputApi } | null = null
let micVad: VoiceInputApi | null = null
let dictateVad: VoiceInputApi | null = null
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

/**
 * Convert the user-selected language preference (`'auto' | 'fr' | 'en'`)
 * into the wire value: `null` for auto-detect (server interprets it as
 * "no override, use default"), the ISO 639-1 code otherwise.
 */
function resolveLanguageHint(): string | undefined {
  return sttLanguage.value === 'auto' ? undefined : sttLanguage.value
}

/**
 * Common option bag passed to <c>useVoiceInput</c> from the chat-mic
 * and the dictate-mic. Single source of derivation (DRY) so both flows
 * stay in lock-step with the user's settings.
 */
function buildVoiceOptions(extra: {
  onSpeechStart: () => void
  onSpeechEnd: (audio: Float32Array) => void | Promise<void>
}): any {
  return {
    mode: inputMode.value,
    threshold: vadThreshold.value,
    negativeSpeechThreshold: negativeSpeechThreshold.value,
    redemptionFrames: redemptionFrames.value,
    deviceId: selectedDeviceId.value,
    audioConstraints: {
      noiseSuppression: noiseSuppression.value,
      echoCancellation: echoCancellation.value,
      autoGainControl: autoGainControl.value,
    },
    ...extra,
  }
}

async function activateMic(): Promise<boolean> {
  if (!voiceInputModule) return false
  if (isMicActive.value) return true

  micError.value = null
  const vad = voiceInputModule.useVoiceInput(buildVoiceOptions({
    onSpeechStart: () => {
      isListening.value = true
    },
    onSpeechEnd: async (audio: Float32Array) => {
      isListening.value = false
      const engine = getPreferredVoiceEngine()
      store.sendVoiceInput(audio, engine, resolveLanguageHint())
    },
  }))

  micVad = vad
  vadStop = vad.stop

  try {
    await vad.start()
    isMicActive.value = true
    return true
  }
  catch (e) {
    micError.value = e instanceof Error ? e.message : 'Microphone access denied'
    isMicActive.value = false
    micVad = null
    return false
  }
}

function deactivateMic(): void {
  vadStop?.()
  vadStop = null
  micVad = null
  isMicActive.value = false
  isListening.value = false
}

/**
 * VAD mode entrypoint — clicking the mic button toggles continuous
 * recording on/off. PTT mode clicks are routed to the press/release
 * handlers below instead.
 */
async function toggleMic(): Promise<void> {
  if (!voiceInputModule) return
  if (inputMode.value !== 'vad') return

  if (isMicActive.value) {
    deactivateMic()
    return
  }

  await activateMic()
}

/**
 * PTT mode mic press. Lazily activates the strategy on first press
 * (acquires the MediaStream once and reuses it across releases) then
 * arms a recording window. Has no effect in VAD mode.
 */
async function pressMic(): Promise<void> {
  if (inputMode.value !== 'ptt') return
  if (!voiceInputModule) return

  const ready = await activateMic()
  if (!ready || !micVad) return
  isListening.value = true
  micVad.press?.()
}

/**
 * PTT mode mic release. Closes the recording window — the strategy
 * decodes + emits <c>onSpeechEnd</c> which sends the message. Safe
 * to call when no recording is in flight (no-op).
 */
function releaseMic(): void {
  if (inputMode.value !== 'ptt') return
  isListening.value = false
  micVad?.release?.()
}

async function activateDictate(): Promise<boolean> {
  if (!voiceInputModule) return false
  if (isDictateActive.value) return true

  micError.value = null
  const vad = voiceInputModule.useVoiceInput(buildVoiceOptions({
    onSpeechStart: () => {
      isDictateListening.value = true
    },
    onSpeechEnd: async (audio: Float32Array) => {
      isDictateListening.value = false
      isDictateBusy.value = true
      try {
        const engine = getPreferredVoiceEngine()
        const text = await store.transcribeVoice(audio, engine, resolveLanguageHint())
        if (!text) return
        // Append (don't replace) so the user can chain several dictation
        // bursts into a single message; insert a separator only when the
        // existing input doesn't already end with whitespace.
        const current = inputText.value
        const sep = current.length > 0 && !/\s$/.test(current) ? ' ' : ''
        inputText.value = current + sep + text
        await nextTick()
        autoResizeTextarea()
        textareaRef.value?.focus()
      }
      catch (e) {
        micError.value = e instanceof Error ? e.message : 'Transcription failed'
      }
      finally {
        isDictateBusy.value = false
      }
    },
  }))

  dictateVad = vad
  dictateVadStop = vad.stop

  try {
    await vad.start()
    isDictateActive.value = true
    return true
  }
  catch (e) {
    micError.value = e instanceof Error ? e.message : 'Microphone access denied'
    isDictateActive.value = false
    dictateVad = null
    return false
  }
}

function deactivateDictate(): void {
  dictateVadStop?.()
  dictateVadStop = null
  dictateVad = null
  isDictateActive.value = false
  isDictateListening.value = false
}

async function toggleDictate(): Promise<void> {
  if (!voiceInputModule) return
  if (inputMode.value !== 'vad') return

  if (isDictateActive.value) {
    deactivateDictate()
    return
  }

  await activateDictate()
}

async function pressDictate(): Promise<void> {
  if (inputMode.value !== 'ptt') return
  const ready = await activateDictate()
  if (!ready || !dictateVad) return
  isDictateListening.value = true
  dictateVad.press?.()
}

function releaseDictate(): void {
  if (inputMode.value !== 'ptt') return
  isDictateListening.value = false
  dictateVad?.release?.()
}

onUnmounted(() => {
  vadStop?.()
  dictateVadStop?.()
})

// ── Attachments (composer state lives in the store) ────────────────────────
const attachmentError = ref<string | null>(null)
const isDraggingOver = ref(false)
let dragDepth = 0

function handleAttachClick(): void {
  pickFilesFromDialog(addFiles)
}

function addFiles(files: File[]): void {
  if (files.length === 0) return
  const result = store.addPendingAttachments(files)
  if (!result.ok) {
    attachmentError.value = result.message
    // Auto-dismiss the error after a few seconds so the composer stays tidy.
    setTimeout(() => {
      if (attachmentError.value === result.message) attachmentError.value = null
    }, 4000)
  }
  else {
    attachmentError.value = null
  }
}

function handleDragEnter(e: DragEvent): void {
  // Only react when the drag carries files — ignore selection drags from
  // inside the textarea to keep the overlay out of the way.
  if (!e.dataTransfer?.types.includes('Files')) return
  dragDepth++
  isDraggingOver.value = true
}

function handleDragLeave(): void {
  dragDepth = Math.max(0, dragDepth - 1)
  if (dragDepth === 0) isDraggingOver.value = false
}

function handleDragOver(e: DragEvent): void {
  if (!e.dataTransfer?.types.includes('Files')) return
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy'
}

function handleDrop(e: DragEvent): void {
  dragDepth = 0
  isDraggingOver.value = false
  const files = extractDropFiles(e.dataTransfer)
  if (files.length === 0) return
  e.preventDefault()
  addFiles(files)
}

function handlePaste(e: ClipboardEvent): void {
  const files = extractPasteFiles(e)
  if (files.length === 0) return
  // Prevent the pasted image from also landing as a data URL in the
  // textarea (browsers sometimes double-dispatch paste events).
  e.preventDefault()
  addFiles(files)
}

onMounted(() => {
  window.addEventListener('paste', handlePaste)
})

onUnmounted(() => {
  window.removeEventListener('paste', handlePaste)
  store.clearPendingAttachments()
})

// ── Text input ──────────────────────────────────────────────────────────────
function handleSend(): void {
  const text = inputText.value.trim()
  const hasText = text.length > 0
  const hasAttachments = store.pendingAttachments.length > 0
  if ((!hasText && !hasAttachments) || store.isStreaming) return
  void store.sendMessage(text)
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

const hasInput = computed(
  () => inputText.value.trim().length > 0 || store.pendingAttachments.length > 0,
)
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
        :class="[
          'chat-bubble',
          `chat-bubble--${msg.role}`,
          msg.errorCode ? 'chat-bubble--error' : null,
        ]"
      >
        <span class="chat-bubble__label">{{ msg.role === 'user' ? (msg.speakerName ?? 'You') : 'Seren' }}</span>
        <template v-if="msg.errorCode">
          <p class="chat-bubble__error-headline">
            {{ $t(`chat.voiceError.${msg.errorCode}`, { _: $t('chat.voiceError.fallback') }) }}
          </p>
          <p v-if="msg.errorMessage" class="chat-bubble__error-detail">
            {{ msg.errorMessage }}
          </p>
        </template>
        <p v-else class="chat-bubble__text">{{ msg.content }}</p>
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

    <!-- Attachment error (auto-dismiss after 4 s) -->
    <div v-if="attachmentError" class="chat-attachment-error">
      {{ attachmentError }}
    </div>

    <!-- Pending attachment chips -->
    <div
      v-if="store.pendingAttachments.length > 0"
      class="chat-attachment-row"
    >
      <ChatAttachmentChip
        v-for="att in store.pendingAttachments"
        :key="att.id"
        :attachment="att"
        @remove="(id) => store.removePendingAttachment(id)"
      />
    </div>

    <!-- Input area -->
    <div
      class="chat-input-area"
      :class="{ 'chat-input-area--drag': isDraggingOver }"
      @dragenter="handleDragEnter"
      @dragleave="handleDragLeave"
      @dragover="handleDragOver"
      @drop="handleDrop"
    >
      <div v-if="isDraggingOver" class="chat-input-area__dropHint">
        Drop to attach
      </div>
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
          class="chat-attach-btn"
          :disabled="store.isStreaming"
          title="Attach a file"
          @click="handleAttachClick"
        >
          <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor">
            <path d="M16.5 6v11.5a4 4 0 0 1-8 0V5a2.5 2.5 0 0 1 5 0v10.5a1 1 0 0 1-2 0V6H10v9.5a2.5 2.5 0 0 0 5 0V5a4 4 0 0 0-8 0v12.5a5.5 5.5 0 0 0 11 0V6h-1.5z" />
          </svg>
        </button>
        <button
          v-if="isMicAvailable"
          :class="['chat-mic-btn', {
            'chat-mic-btn--active': isMicActive,
            'chat-mic-btn--listening': isListening,
            'chat-mic-btn--ptt': inputMode === 'ptt',
          }]"
          :title="inputMode === 'ptt'
            ? (isListening ? 'Relâche pour envoyer' : 'Maintiens pour parler')
            : (isMicActive ? 'Stop microphone' : 'Start microphone')"
          @click="toggleMic"
          @pointerdown="pressMic"
          @pointerup="releaseMic"
          @pointerleave="releaseMic"
          @pointercancel="releaseMic"
        >
          <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor">
            <path v-if="!isMicActive" d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm-1-9c0-.55.45-1 1-1s1 .45 1 1v6c0 .55-.45 1-1 1s-1-.45-1-1V5zm6 6c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z" />
            <path v-else d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5-3c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z" />
          </svg>
        </button>
        <button
          v-if="isMicAvailable"
          :class="['chat-dictate-btn', {
            'chat-dictate-btn--active': isDictateActive,
            'chat-dictate-btn--listening': isDictateListening,
            'chat-dictate-btn--busy': isDictateBusy,
            'chat-dictate-btn--ptt': inputMode === 'ptt',
          }]"
          :title="inputMode === 'ptt'
            ? (isDictateListening ? 'Relâche pour transcrire' : 'Maintiens pour dicter')
            : (isDictateActive
              ? 'Arrêter la dictée'
              : 'Dicter dans la zone de texte (sans envoyer)')"
          :disabled="isDictateBusy"
          @click="toggleDictate"
          @pointerdown="pressDictate"
          @pointerup="releaseDictate"
          @pointerleave="releaseDictate"
          @pointercancel="releaseDictate"
        >
          <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor" aria-hidden="true">
            <!-- Mic + text-lines glyph: signals "voice → text" without sending. -->
            <path d="M12 13c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v5c0 1.66 1.34 3 3 3zM9 5c0-1.66 1.34-3 3-3s3 1.34 3 3v5c0 1.66-1.34 3-3 3" fill="none" stroke="currentColor" stroke-width="0.7"/>
            <path d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3z" />
            <path d="M5 10h2c0 2.76 2.24 5 5 5v2c-3.86 0-7-3.14-7-7z" />
            <rect x="14" y="14" width="6" height="1.4" rx="0.7" />
            <rect x="14" y="17" width="6" height="1.4" rx="0.7" />
            <rect x="14" y="20" width="4" height="1.4" rx="0.7" />
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

/* ── Voice transcription error bubble ────────────────────────────── */
.chat-bubble--error {
  background: oklch(0.62 0.18 25 / 0.12);
  border: 1px solid oklch(0.62 0.18 25 / 0.45);
  color: oklch(0.85 0.08 25);
}

.chat-bubble--error .chat-bubble__label {
  color: oklch(0.78 0.16 25);
}

.chat-bubble__error-headline {
  margin: 0;
  font-size: 0.88rem;
  font-weight: 500;
  white-space: pre-wrap;
}

.chat-bubble__error-detail {
  margin: 0.25rem 0 0 0;
  font-size: 0.74rem;
  opacity: 0.75;
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
.chat-attachment-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  padding: 0 0.2rem 0.5rem 0.2rem;
}
.chat-attachment-error {
  color: #fca5a5;
  font-size: 0.75rem;
  padding: 0.25rem 0.4rem;
  background: rgba(239, 68, 68, 0.08);
  border-radius: 6px;
  margin-bottom: 0.25rem;
}
.chat-input-area--drag {
  outline: 2px dashed oklch(0.65 0.18 200);
  outline-offset: -3px;
}
.chat-input-area__dropHint {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: oklch(0 0 0 / 0.25);
  color: #e2e8f0;
  font-size: 0.85rem;
  pointer-events: none;
  border-radius: 12px;
  z-index: 2;
}
.chat-attach-btn {
  width: 32px;
  height: 32px;
  border-radius: 6px;
  border: none;
  background: transparent;
  color: #94a3b8;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
}
.chat-attach-btn:hover:not(:disabled) {
  background: oklch(0.65 0.18 200 / 0.12);
  color: #e2e8f0;
}
.chat-attach-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
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
.chat-dictate-btn,
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
.chat-dictate-btn:hover,
.chat-action-btn:hover {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.18);
  color: var(--airi-text);
}

.chat-send-btn:active,
.chat-mic-btn:active,
.chat-dictate-btn:active,
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

/* Dictate button — distinct teal/seren-accent palette so the user can
 * tell at a glance that this captures voice into the textarea (vs. the
 * red mic above which sends straight to chat). */
.chat-dictate-btn--active {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.25);
  color: oklch(0.92 0.05 var(--seren-hue));
}

.chat-dictate-btn--active:hover {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.4);
  color: oklch(0.95 0.05 var(--seren-hue));
}

.chat-dictate-btn--listening {
  background: oklch(0.74 0.127 var(--seren-hue) / 0.45);
  color: oklch(0.95 0.05 var(--seren-hue));
  animation: pulse 1.2s ease-in-out infinite;
}

.chat-dictate-btn--busy {
  opacity: 0.6;
  cursor: progress;
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
