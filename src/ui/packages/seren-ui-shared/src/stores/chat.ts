import type {
  AudioPlaybackPayload,
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

  // ── Audio / lipsync state ────────────────────────────────────────────
  const lipsyncFrames = ref<LipsyncFramePayload[]>([])
  const audioChunks = ref<AudioPlaybackPayload[]>([])
  const isSpeaking = ref(false)
  const lastError = ref<string | null>(null)
  // True while the model is streaming its chain-of-thought (before the
  // actual answer starts). Drives the animated thinking indicator.
  const isThinking = ref(false)

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
        })
        currentAssistantContent.value = ''
      }
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
      const last = messages.value.at(-1)
      if (last && last.role === 'assistant') {
        last.emotion = data.emotion
      }
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
    client.value.send(EventTypes.InputText, { text })
  }

  function sendVoiceInput(audio: Float32Array): void {
    if (!client.value || client.value.currentStatus !== 'ready') {
      console.warn('Cannot send voice input: client not ready')
      return
    }

    const audioData = encodeWavBase64(audio, VOICE_SAMPLE_RATE)
    client.value.send(EventTypes.InputVoice, { audioData, format: 'wav' })
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
