import type {
  AvatarEmotionPayload,
  ChatChunkPayload,
  ChatEndPayload,
  ClientStatus,
  WebSocketFactory,
} from '@seren/sdk'
import { Client, EventTypes } from '@seren/sdk'
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

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
  const messages = ref<ChatMessage[]>([])
  const client = ref<Client | null>(null)
  const connectionStatus = ref<ClientStatus>('idle')
  const currentAssistantContent = ref('')
  const isStreaming = ref(false)

  const lastMessage = computed(() => messages.value.at(-1))
  const messageCount = computed(() => messages.value.length)

  function initClient(url: string, optionsOrToken?: string | InitClientOptions): void {
    if (client.value) client.value.disconnect()

    const resolved: InitClientOptions
      = typeof optionsOrToken === 'string' ? { token: optionsOrToken } : (optionsOrToken ?? {})

    const c = new Client({
      url,
      token: resolved.token,
      webSocketFactory: resolved.webSocketFactory,
    })
    client.value = c

    // Listen for chat chunks
    c.onEvent<ChatChunkPayload>(EventTypes.OutputChatChunk, (data) => {
      currentAssistantContent.value += data.content
      isStreaming.value = true
    })

    // Listen for chat end
    c.onEvent<ChatEndPayload>(EventTypes.OutputChatEnd, () => {
      if (currentAssistantContent.value) {
        messages.value.push({
          id: crypto.randomUUID(),
          role: 'assistant',
          content: currentAssistantContent.value,
          timestamp: Date.now(),
        })
        currentAssistantContent.value = ''
      }
      isStreaming.value = false
    })

    // Listen for avatar emotions
    c.onEvent<AvatarEmotionPayload>(EventTypes.AvatarEmotion, (data) => {
      const last = messages.value.at(-1)
      if (last && last.role === 'assistant') {
        last.emotion = data.emotion
      }
    })

    // Listen for errors
    c.onEvent(EventTypes.Error, (data) => {
      console.error('Seren error:', data)
    })

    c.connect()
  }

  function sendMessage(text: string): void {
    if (!client.value || client.value.currentStatus !== 'ready') {
      console.warn('Cannot send message: client not ready')
      return
    }

    // Add user message to local state
    messages.value.push({
      id: crypto.randomUUID(),
      role: 'user',
      content: text,
      timestamp: Date.now(),
    })

    // Send to hub
    currentAssistantContent.value = ''
    client.value.send('input:text', { text })
  }

  function disconnect(): void {
    if (client.value) {
      client.value.disconnect()
      client.value = null
      connectionStatus.value = 'idle'
    }
  }

  function clearMessages(): void {
    messages.value = []
    currentAssistantContent.value = ''
  }

  return {
    messages,
    client,
    connectionStatus,
    currentAssistantContent,
    isStreaming,
    lastMessage,
    messageCount,
    initClient,
    sendMessage,
    disconnect,
    clearMessages,
  }
})