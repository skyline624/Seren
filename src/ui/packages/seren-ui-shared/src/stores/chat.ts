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
  ChatProviderDegradedPayload,
  ClientStatus,
  LipsyncFramePayload,
  StreamErrorCategory,
  UserEchoPayload,
  WebSocketFactory,
} from '@seren/sdk'
import { Client, EventTypes, generateId } from '@seren/sdk'
import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { useSettingsStore } from './settings'
import { useAnimationSettingsStore } from './settings/animation'
import { avatarDebugLog } from '../composables/avatarDebugLog'
import {
  createTransformersEmotionClassifier,
  NoopEmotionClassifier,
  type ITextEmotionClassifier,
} from '../composables/useTextEmotionClassifier'
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
  /**
   * Id of the run currently streaming. Set when `sendMessage` mints the
   * clientMessageId (which is also the OpenClaw `runId`), cleared on
   * `output:chat:end` or on a stream error. Drives the Stop button:
   * the UI sends this back as `input:chat:abort.runId` so the hub
   * cancels the right run even if multiple turns race.
   */
  const currentRunId = ref<string | null>(null)
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
  /**
   * Surface the server's error taxonomy so the UI can pick a remediation
   * affordance (Retry button for transient, info banner for degraded,
   * support link for permanent). `null` when nothing's wrong.
   */
  const lastError = ref<{
    message: string
    category?: StreamErrorCategory
    code?: string
    failedProvider?: string
  } | null>(null)
  /**
   * Non-terminal "we're transparently switching providers" notice.
   * Replaced on each new degradation event; cleared at the next chat:end
   * or when a new message is sent.
   */
  const degradationNotice = ref<ChatProviderDegradedPayload | null>(null)
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
  /**
   * Active emotion to display on the avatar.
   *  - `emotion`  : semantic name (e.g. `joy`, `sad`). Aliases
   *                 resolved downstream by `useVRMEmote`.
   *  - `nonce`    : bumped on every set so the watcher re-triggers
   *                 even when the same emotion fires twice in a row.
   *  - `intensity`: blendshape peak multiplier in [0, 1]. Explicit
   *                 LLM markers stamp 1.0 (fully authoritative) ;
   *                 the Tier-2 text classifier stamps its confidence
   *                 score so low-signal predictions produce subtler
   *                 facial motion.
   */
  const currentEmotion = ref<{ emotion: string, nonce: number, intensity?: number } | null>(null)
  let pendingEmotion: string | null = null
  // ── Text emotion classifier (Tier 2) ──────────────────────────────
  // Tracks whether the LLM has explicitly marked the current message
  // with `<emotion:xxx>`. While true, the classifier is skipped —
  // explicit markers always win over inferred emotion.
  let hasExplicitEmotionInCurrentMessage = false
  let classifier: ITextEmotionClassifier = new NoopEmotionClassifier()
  let classifyInFlight = false
  let lastClassifyAt = 0
  /** Minimum gap between classifier inferences within a single stream.
   *  DistilBERT is ~150-300 ms on CPU; throttling to once every 3 s
   *  keeps main-thread cost negligible even on low-end hardware. */
  const CLASSIFY_MIN_INTERVAL_MS = 3000
  /** Don't bother classifying below this length — accuracy drops hard
   *  on 1-10 char fragments and early chunks are typically filler. */
  const CLASSIFY_MIN_TEXT_LENGTH = 20
  const animationSettings = useAnimationSettingsStore()

  // Lazily spin up the transformers.js worker only when the user opts in.
  watch(
    () => animationSettings.classifierEnabled,
    async (enabled) => {
      classifier.dispose()
      if (enabled) {
        classifier = createTransformersEmotionClassifier({
          confidenceThreshold: animationSettings.classifierConfidenceThreshold,
        })
        try {
          await classifier.init()
          avatarDebugLog('classifier', 'ready')
        }
        catch (err) {
          avatarDebugLog('classifier', 'init_failed', {
            error: err instanceof Error ? err.message : String(err),
          })
          // Fall back to Noop so subsequent calls are silent no-ops.
          classifier = new NoopEmotionClassifier()
        }
      }
      else {
        classifier = new NoopEmotionClassifier()
      }
    },
    { immediate: true },
  )

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
      currentRunId.value = null
      degradationNotice.value = null
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
      currentRunId.value = null
      degradationNotice.value = null
      clearAudioState()
    })

    // ── Live chat events ─────────────────────────────────────────────
    c.onEvent<ChatChunkPayload>(EventTypes.OutputChatChunk, (data) => {
      // Defensive: ignore empty/malformed chunks instead of appending "undefined"
      if (typeof data?.content === 'string' && data.content.length > 0) {
        currentAssistantContent.value += data.content
      }
      isStreaming.value = true

      // Fire-and-forget: classify the accumulated text when no explicit
      // emotion marker has landed for this message and the classifier
      // is enabled + not already busy + rate-limit window elapsed.
      // Intentionally not awaited — streaming must not block on inference.
      maybeClassifyForCurrentMessage()
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
      currentRunId.value = null
      // Re-arm the classifier for the next message.
      hasExplicitEmotionInCurrentMessage = false
      lastClassifyAt = 0
      // Clear the transient switching notice on stream close — if we
      // successfully recovered via retry/fallback the answer is in, no
      // reason to keep "switching…" visible.
      degradationNotice.value = null
    })

    // Informational event — pipeline transparently retried / fell back.
    // Never closes the stream; always followed by chat:end eventually.
    c.onEvent<ChatProviderDegradedPayload>(EventTypes.OutputChatProviderDegraded, (data) => {
      degradationNotice.value = data
    })

    // ── User-turn echo (multi-tab sync) ──────────────────────────────
    // The hub broadcasts `output:chat:user` to every peer except the
    // sender. The originating tab already has this message in its store
    // under `messageId` (see `sendMessage`), so the `some(…)` check
    // short-circuits silently for self-echoes. Other tabs insert the
    // bubble so their view matches the sender's.
    c.onEvent<UserEchoPayload>(EventTypes.OutputChatUser, (data) => {
      if (messages.value.some(m => m.id === data.messageId)) return
      messages.value.push({
        id: data.messageId,
        role: 'user',
        content: data.text,
        timestamp: data.timestampMs,
      })
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
      // Explicit LLM marker → authoritative. Gate the classifier off
      // for the remainder of this message (re-armed at chat:end).
      hasExplicitEmotionInCurrentMessage = true
      // Emotion events typically fire during streaming — before the
      // assistant message is pushed at chat:end. Buffer the emotion so
      // we can attach it when the message lands, and expose it live so
      // the avatar reacts immediately.
      pendingEmotion = data.emotion
      // Explicit LLM marker → intensity 1.0 (fully authoritative).
      currentEmotion.value = { emotion: data.emotion, nonce: Date.now(), intensity: 1 }
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
    c.onEvent(EventTypes.Error, (data: { message?: string, code?: string, category?: StreamErrorCategory, failedProvider?: string }) => {
      // Stream-stall codes always arrive immediately before `output:chat:end`,
      // so the assistant bubble (whatever was buffered) is preserved by the
      // chat:end handler — we only surface the error here. The rest of the
      // teardown (isStreaming/isThinking/currentRunId reset) runs below.
      const message = data?.message ?? 'Unknown error'
      lastError.value = {
        message,
        category: data?.category,
        code: data?.code,
        failedProvider: data?.failedProvider,
      }
      if (data?.code === 'stream_idle_timeout' || data?.code === 'stream_total_timeout') {
        console.warn('Seren stream stalled:', data.code, data?.category, message)
      }
      else {
        console.error('Seren error:', data?.category, message)
      }
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

  /**
   * Conditionally fire a text-emotion classification on the current
   * assistant content. Fire-and-forget — never awaited in a hot path.
   *
   * Skip criteria (in order):
   * - Classifier disabled (NoopEmotionClassifier always returns null).
   * - An explicit `<emotion:xxx>` marker already fired for this message.
   * - Another inference is still in flight (no parallel calls).
   * - Last inference happened < CLASSIFY_MIN_INTERVAL_MS ago.
   * - Accumulated text too short to classify reliably.
   */
  function maybeClassifyForCurrentMessage(): void {
    if (hasExplicitEmotionInCurrentMessage) return
    if (classifyInFlight) return
    const now = Date.now()
    if (now - lastClassifyAt < CLASSIFY_MIN_INTERVAL_MS) return
    const text = currentAssistantContent.value
    if (text.length < CLASSIFY_MIN_TEXT_LENGTH) return

    classifyInFlight = true
    lastClassifyAt = now
    classifier.classify(text)
      .then((prediction) => {
        // Re-check the guard: an explicit marker may have landed while
        // the worker was busy. The LLM always wins.
        if (prediction && !hasExplicitEmotionInCurrentMessage) {
          pendingEmotion = prediction.emotion
          // Classifier → intensity driven by its confidence score.
          // Clamped to [0, 1] so downstream composables never see
          // out-of-range values.
          const intensity = Math.max(0, Math.min(1, prediction.score))
          currentEmotion.value = {
            emotion: prediction.emotion,
            nonce: Date.now(),
            intensity,
          }
          avatarDebugLog('classifier', 'emit', {
            emotion: prediction.emotion,
            score: prediction.score,
            textLength: text.length,
          })
        }
      })
      .catch((err) => {
        avatarDebugLog('classifier', 'error', {
          error: err instanceof Error ? err.message : String(err),
        })
      })
      .finally(() => {
        classifyInFlight = false
      })
  }

  function sendMessage(text: string): void {
    if (!client.value || client.value.currentStatus !== 'ready') {
      console.warn('Cannot send message: client not ready')
      return
    }

    // Reset per-message classifier gates so the next stream starts fresh.
    hasExplicitEmotionInCurrentMessage = false
    lastClassifyAt = 0

    // Mint the id once and use it for both the optimistic bubble AND the
    // outbound payload. The hub echoes it back to every other peer via
    // `output:chat:user`; those peers render the bubble under the same
    // id, while this tab ignores the echo because `messages` already
    // contains a message with that id (see the OutputChatUser handler).
    const clientMessageId = generateId()

    messages.value.push({
      id: clientMessageId,
      role: 'user',
      content: text,
      timestamp: Date.now(),
    })

    currentAssistantContent.value = ''
    // Set isStreaming + currentRunId optimistically so the Stop button
    // appears the moment the user hits Send, not only after the first
    // chunk lands. The id mirrors the upstream OpenClaw runId because
    // the hub uses clientMessageId as its idempotencyKey.
    isStreaming.value = true
    lastError.value = null
    degradationNotice.value = null
    currentRunId.value = clientMessageId
    const settings = useSettingsStore()
    client.value.send(EventTypes.InputText, {
      text,
      model: settings.llmModel,
      clientMessageId,
    })
  }

  /**
   * Re-submit the most recent user message after an error. Clears
   * `lastError` optimistically so the popup disappears before the
   * network round-trip. No-op if there's no user message to replay.
   */
  function retryLastMessage(): void {
    const lastUser = [...messages.value].reverse().find(m => m.role === 'user')
    if (!lastUser) return
    lastError.value = null
    void sendMessage(lastUser.content)
  }

  /**
   * Ask the hub to cancel the current chat run. The hub forwards the
   * abort to OpenClaw and emits `output:chat:end` from its teardown
   * path, which is what flips `isStreaming` back to false — so this
   * function intentionally does not touch the streaming flags itself.
   */
  function abortStream(): void {
    if (!client.value || client.value.currentStatus !== 'ready') {
      return
    }
    if (!isStreaming.value) {
      return
    }
    client.value.send(EventTypes.InputChatAbort, {
      runId: currentRunId.value ?? undefined,
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
    currentRunId,
    initClient,
    sendMessage,
    retryLastMessage,
    abortStream,
    loadMoreHistory,
    resetConversation,
    disconnect,
    clearMessages,
    lastError,
    degradationNotice,
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
