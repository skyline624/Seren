import type {
  AudioPlaybackPayload,
  AvatarActionPayload,
  AvatarEmotionPayload,
  ChatChunkPayload,
  ChatEndPayload,
  ClientStatus,
  LipsyncFramePayload,
  WebSocketFactory,
} from '@seren/sdk'
import { Client, EventTypes, generateId } from '@seren/sdk'
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { encodeWavBase64 } from '../utils/wav-encoder'

/** Sample rate expected by the server STT pipeline. */
const VOICE_SAMPLE_RATE = 16000

export interface ChatMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
  timestamp: number
  emotion?: string
}

export interface InitClientOptions {
  token?: string
  webSocketFactory?: WebSocketFactory
}

export const useChatStore = defineStore('chat', () => {
  // ── Text chat state ──────────────────────────────────────────────────
  const messages = ref<ChatMessage[]>([])
  const client = ref<Client | null>(null)
  const connectionStatus = ref<ClientStatus>('idle')
  const currentAssistantContent = ref('')
  const isStreaming = ref(false)
  // Tab-scoped conversation identifier. Sent with every `input:text` so
  // the gateway (OpenClaw) routes consecutive turns into the same session,
  // preserving multi-turn context. A reload generates a fresh id — by
  // design, we don't persist it across reloads.
  const sessionId = ref(crypto.randomUUID())

  // ── Audio / lipsync state ────────────────────────────────────────────
  const lipsyncFrames = ref<LipsyncFramePayload[]>([])
  const audioChunks = ref<AudioPlaybackPayload[]>([])
  const isSpeaking = ref(false)
  const lastError = ref<string | null>(null)
  // True while the model is streaming its chain-of-thought (before the
  // actual answer starts). Drives the animated thinking indicator.
  const isThinking = ref(false)
  // Last gesture extracted from an <action:xxx> marker. The `nonce` is
  // bumped on every event so repeated identical actions (e.g. two waves
  // in a row) still re-trigger a watcher downstream.
  const currentAction = ref<{ action: string, nonce: number } | null>(null)
  // Last emotion received during the current stream. Attached to the
  // assistant message at `OutputChatEnd` because emotion events arrive
  // before the message is pushed. Also exposed as a live ref so the
  // avatar can react mid-stream.
  const currentEmotion = ref<{ emotion: string, nonce: number } | null>(null)
  let pendingEmotion: string | null = null

  let statusInterval: ReturnType<typeof setInterval> | null = null

  const lastMessage = computed(() => messages.value.at(-1))
  const messageCount = computed(() => messages.value.length)

  function initClient(url: string, optionsOrToken?: string | InitClientOptions): void {
    if (client.value) {
      client.value.disconnect()
    }

    const resolved: InitClientOptions
      = typeof optionsOrToken === 'string' ? { token: optionsOrToken } : (optionsOrToken ?? {})

    const c = new Client({
      url,
      token: resolved.token,
      webSocketFactory: resolved.webSocketFactory,
    })
    client.value = c

    // ── Chat events ──────────────────────────────────────────────────
    c.onEvent<ChatChunkPayload>(EventTypes.OutputChatChunk, (data) => {
      // Defensive: ignore empty/malformed chunks instead of appending "undefined"
      if (typeof data?.content === 'string' && data.content.length > 0) {
        currentAssistantContent.value += data.content
      }
      isStreaming.value = true
    })

    c.onEvent<ChatEndPayload>(EventTypes.OutputChatEnd, () => {
      if (currentAssistantContent.value) {
        messages.value.push({
          id: generateId(),
          role: 'assistant',
          content: currentAssistantContent.value,
          timestamp: Date.now(),
          emotion: pendingEmotion ?? undefined,
        })
        currentAssistantContent.value = ''
      }
      pendingEmotion = null
      isStreaming.value = false
      isThinking.value = false
    })

    // ── Thinking indicator (reasoning / chain-of-thought) ────────────
    c.onEvent(EventTypes.OutputChatThinkingStart, () => {
      isThinking.value = true
      isStreaming.value = true
    })

    c.onEvent(EventTypes.OutputChatThinkingEnd, () => {
      isThinking.value = false
    })

    c.onEvent<AvatarEmotionPayload>(EventTypes.AvatarEmotion, (data) => {
      // Emotion events typically fire during streaming — before the
      // assistant message is pushed at chat:end. Buffer the emotion so
      // we can attach it when the message lands, and expose it live so
      // the avatar reacts immediately.
      pendingEmotion = data.emotion
      currentEmotion.value = { emotion: data.emotion, nonce: Date.now() }
      const last = messages.value.at(-1)
      if (last && last.role === 'assistant') {
        last.emotion = data.emotion
      }
    })

    c.onEvent<AvatarActionPayload>(EventTypes.AvatarAction, (data) => {
      // Bump nonce so repeated identical actions still trigger downstream
      // watchers (e.g. wave twice in a row).
      currentAction.value = { action: data.action, nonce: Date.now() }
    })

    // ── Audio playback events ────────────────────────────────────────
    c.onEvent<AudioPlaybackPayload>(EventTypes.AudioPlaybackChunk, (data) => {
      audioChunks.value.push(data)
      isSpeaking.value = true
    })

    c.onEvent<LipsyncFramePayload>(EventTypes.AudioLipsyncFrame, (data) => {
      lipsyncFrames.value.push(data)
    })

    // ── Errors ───────────────────────────────────────────────────────
    c.onEvent(EventTypes.Error, (data: { message?: string }) => {
      const msg = data?.message ?? 'Unknown error'
      lastError.value = msg
      console.error('Seren error:', msg)
    })

    c.connect()

    // Sync SDK connection status → reactive ref
    if (statusInterval) clearInterval(statusInterval)
    statusInterval = setInterval(() => {
      if (client.value) {
        connectionStatus.value = client.value.currentStatus
      }
    }, 500)
  }

  function sendMessage(text: string): void {
    if (!client.value || client.value.currentStatus !== 'ready') {
      console.warn('Cannot send message: client not ready')
      return
    }

    messages.value.push({
      id: generateId(),
      role: 'user',
      content: text,
      timestamp: Date.now(),
    })

    currentAssistantContent.value = ''
    client.value.send(EventTypes.InputText, { text, sessionId: sessionId.value })
  }

  function sendVoiceInput(audio: Float32Array): void {
    if (!client.value || client.value.currentStatus !== 'ready') {
      console.warn('Cannot send voice input: client not ready')
      return
    }

    const audioData = encodeWavBase64(audio, VOICE_SAMPLE_RATE)
    client.value.send(EventTypes.InputVoice, {
      audioData,
      format: 'wav',
      sessionId: sessionId.value,
    })
  }

  /** Rotate the sessionId so the next turn starts a fresh server-side
   * conversation. The UI message list isn't cleared — wire a button to
   * combine this with `clearMessages()` if you need a hard reset. */
  function startNewConversation(): void {
    sessionId.value = crypto.randomUUID()
  }

  /** Consume and clear pending audio chunks (for PlaybackManager integration). */
  function flushAudioChunks(): AudioPlaybackPayload[] {
    const chunks = [...audioChunks.value]
    audioChunks.value = []
    return chunks
  }

  /** Consume and clear pending lipsync frames (for avatar rendering). */
  function flushLipsyncFrames(): LipsyncFramePayload[] {
    const frames = [...lipsyncFrames.value]
    lipsyncFrames.value = []
    return frames
  }

  function clearAudioState(): void {
    audioChunks.value = []
    lipsyncFrames.value = []
    isSpeaking.value = false
  }

  function disconnect(): void {
    if (statusInterval) {
      clearInterval(statusInterval)
      statusInterval = null
    }
    if (client.value) {
      client.value.disconnect()
      client.value = null
      connectionStatus.value = 'idle'
    }
    clearAudioState()
  }

  function clearMessages(): void {
    messages.value = []
    currentAssistantContent.value = ''
    clearAudioState()
  }

  return {
    // Text chat
    messages,
    client,
    connectionStatus,
    currentAssistantContent,
    isStreaming,
    isThinking,
    currentAction,
    currentEmotion,
    sessionId,
    startNewConversation,
    lastMessage,
    messageCount,
    initClient,
    sendMessage,
    disconnect,
    clearMessages,
    lastError,
    // Voice / audio
    lipsyncFrames,
    audioChunks,
    isSpeaking,
    sendVoiceInput,
    flushAudioChunks,
    flushLipsyncFrames,
    clearAudioState,
  }
})
