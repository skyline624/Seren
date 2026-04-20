import type {
  AudioPlaybackPayload,
  AvatarActionPayload,
  AvatarEmotionPayload,
  ChatChunkPayload,
  ChatClearedPayload,
  ChatEndPayload,
  ChatHistoryBeginPayload,
  ChatHistoryEndPayload,
  ChatHistoryItemPayload,
  ClientStatus,
  LipsyncFramePayload,
  WebSocketFactory,
} from '@seren/sdk'
import { Client, EventTypes, generateId } from '@seren/sdk'
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { useSettingsStore } from './settings'
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
  // The session is server-managed: a single key (`OpenClaw:MainSessionKey`)
  // is shared by every client connected to this Seren instance, so all
  // devices see the same conversation. The hub hydrates new peers from
  // OpenClaw's persisted transcript when they announce.

  // ── History hydration state ──────────────────────────────────────────
  /** True after the initial `output:chat:history:end` arrives. */
  const historyLoaded = ref(false)
  /** True while waiting for `output:chat:history:end` (initial or scroll-back). */
  const historyLoading = ref(false)
  /** Server says more historical messages exist beyond what we've seen. */
  const historyHasMore = ref(true)
  /** Cursor for the next scroll-back request. Bound to the oldest visible message id. */
  const oldestMessageId = ref<string | null>(null)

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

    // ── History hydration events ─────────────────────────────────────
    // The server emits `output:chat:history:begin` immediately before
    // the first item of an initial hydration batch (not for scroll-back).
    // This is the authoritative signal that a fresh burst is about to
    // arrive: drop any locally-displayed messages so re-hydrated items
    // don't collide with live messages that were pushed with a
    // client-generated id. We intentionally tie this to a history event
    // (not to `module:announced`) because the transport-level announce
    // response is serialized AFTER the history items on the socket —
    // clearing on announce would wipe the batch we just received.
    c.onEvent<ChatHistoryBeginPayload>(EventTypes.OutputChatHistoryBegin, (data) => {
      if (data.reset === false) return
      messages.value = []
      currentAssistantContent.value = ''
      pendingEmotion = null
      isStreaming.value = false
      isThinking.value = false
      historyLoaded.value = false
      historyLoading.value = true
      historyHasMore.value = true
      oldestMessageId.value = null
    })

    // Server pushes one item per persisted message, right after a
    // `history:begin` (initial hydration) or in response to an explicit
    // scroll-back request. Items arrive in chronological order (oldest
    // → newest) within a batch; we splice by timestamp so older
    // paginated batches land above what's already visible.
    c.onEvent<ChatHistoryItemPayload>(EventTypes.OutputChatHistoryItem, (data) => {
      // Skip duplicates: a peer that connects mid-stream may receive a
      // history item *and* the live chunks for the same message.
      if (messages.value.some(m => m.id === data.messageId)) {
        return
      }
      const msg: ChatMessage = {
        id: data.messageId,
        role: data.role === 'system' ? 'assistant' : data.role,
        content: data.content,
        timestamp: data.timestamp,
        emotion: data.emotion,
      }
      // Determine where to insert by timestamp: simpler than tracking
      // batch state and cheap for typical history sizes (≤200 messages).
      const insertAt = messages.value.findIndex(m => m.timestamp > msg.timestamp)
      if (insertAt < 0) {
        messages.value.push(msg)
      }
      else {
        messages.value.splice(insertAt, 0, msg)
      }
    })

    c.onEvent<ChatHistoryEndPayload>(EventTypes.OutputChatHistoryEnd, (data) => {
      historyLoaded.value = true
      historyLoading.value = false
      historyHasMore.value = data.hasMore
      if (data.oldestMessageId) {
        oldestMessageId.value = data.oldestMessageId
      }
    })

    c.onEvent<ChatClearedPayload>(EventTypes.OutputChatCleared, () => {
      messages.value = []
      currentAssistantContent.value = ''
      historyLoaded.value = true
      historyHasMore.value = false
      oldestMessageId.value = null
      pendingEmotion = null
      isStreaming.value = false
      isThinking.value = false
      clearAudioState()
    })

    // ── Live chat events ─────────────────────────────────────────────
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

    // Reset hydration state for the new connection — the server pushes a
    // fresh hydration burst when this client announces.
    historyLoaded.value = false
    historyLoading.value = true
    historyHasMore.value = true
    oldestMessageId.value = null

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
    const settings = useSettingsStore()
    client.value.send(EventTypes.InputText, {
      text,
      model: settings.llmModel,
    })
  }

  function sendVoiceInput(audio: Float32Array): void {
    if (!client.value || client.value.currentStatus !== 'ready') {
      console.warn('Cannot send voice input: client not ready')
      return
    }

    const settings = useSettingsStore()
    const audioData = encodeWavBase64(audio, VOICE_SAMPLE_RATE)
    client.value.send(EventTypes.InputVoice, {
      audioData,
      format: 'wav',
      model: settings.llmModel,
    })
  }

  /** Load older messages above the current oldest visible message. */
  function loadMoreHistory(): void {
    if (!client.value || client.value.currentStatus !== 'ready') {
      return
    }
    if (historyLoading.value || !historyHasMore.value) {
      return
    }
    historyLoading.value = true
    client.value.send(EventTypes.InputChatHistoryRequest, {
      before: oldestMessageId.value ?? undefined,
      limit: 30,
    })
  }

  /** Ask the server to reset the conversation (clears LLM context for every
   * connected client; long-term memory and pairing untouched). */
  function resetConversation(): void {
    if (!client.value || client.value.currentStatus !== 'ready') {
      return
    }
    client.value.send(EventTypes.InputChatReset, {})
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
    historyLoaded,
    historyLoading,
    historyHasMore,
    oldestMessageId,
    lastMessage,
    messageCount,
    initClient,
    sendMessage,
    loadMoreHistory,
    resetConversation,
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
