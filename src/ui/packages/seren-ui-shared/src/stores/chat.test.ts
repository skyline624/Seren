import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// Mock @seren/sdk before importing the store. We need a controllable Client
// so we can assert what the store does when events arrive.
const emittedHandlers = new Map<string, (data: unknown) => void>()
let lastClientOptions: unknown = null

vi.mock('@seren/sdk', () => {
  class FakeClient {
    public currentStatus = 'idle' as string

    constructor(options: unknown) {
      lastClientOptions = options
    }

    onEvent(type: string, handler: (data: unknown) => void): () => void {
      emittedHandlers.set(type, handler)
      return () => emittedHandlers.delete(type)
    }

    send(_type: string, _data: unknown): void {}
    connect(): void {
      this.currentStatus = 'ready'
    }

    disconnect(): void {
      this.currentStatus = 'idle'
    }
  }

  let idCounter = 0
  return {
    Client: FakeClient,
    EventTypes: {
      OutputChatChunk: 'output:chat:chunk',
      OutputChatEnd: 'output:chat:end',
      OutputChatThinkingStart: 'output:chat:thinking:start',
      OutputChatThinkingEnd: 'output:chat:thinking:end',
      AvatarEmotion: 'avatar:emotion',
      AvatarAction: 'avatar:action',
      AudioPlaybackChunk: 'audio:playback:chunk',
      AudioLipsyncFrame: 'audio:lipsync:frame',
      Error: 'error',
    },
    generateId: () => `test-id-${++idCounter}`,
  }
})

const { useChatStore } = await import('./chat')

describe('useChatStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    emittedHandlers.clear()
    lastClientOptions = null
  })

  it('InitClient_WithToken_ShouldPassTokenToClient', () => {
    // arrange
    const store = useChatStore()

    // act
    store.initClient('ws://localhost:5000/ws', 'my-token')

    // assert
    expect(lastClientOptions).toMatchObject({
      url: 'ws://localhost:5000/ws',
      token: 'my-token',
    })
  })

  it('InitClient_WithOptionsObject_ShouldForwardFactory', () => {
    // arrange
    const store = useChatStore()
    const fakeFactory = vi.fn()

    // act
    store.initClient('ws://localhost:5000/ws', {
      token: 't',
      webSocketFactory: fakeFactory,
    })

    // assert
    expect(lastClientOptions).toMatchObject({
      url: 'ws://localhost:5000/ws',
      token: 't',
      webSocketFactory: fakeFactory,
    })
  })

  it('OnChatChunk_WhenReceived_ShouldAccumulateStreamingContent', () => {
    // arrange
    const store = useChatStore()
    store.initClient('ws://localhost:5000/ws')
    const chunkHandler = emittedHandlers.get('output:chat:chunk')!

    // act
    chunkHandler({ content: 'Hello ' })
    chunkHandler({ content: 'world!' })

    // assert
    expect(store.currentAssistantContent).toBe('Hello world!')
    expect(store.isStreaming).toBe(true)
  })

  it('OnChatEnd_AfterStreaming_ShouldPushAssistantMessageAndClearBuffer', () => {
    // arrange
    const store = useChatStore()
    store.initClient('ws://localhost:5000/ws')
    const chunkHandler = emittedHandlers.get('output:chat:chunk')!
    const endHandler = emittedHandlers.get('output:chat:end')!
    chunkHandler({ content: 'Bonjour' })

    // act
    endHandler({})

    // assert
    expect(store.messages).toHaveLength(1)
    expect(store.messages[0]).toMatchObject({ role: 'assistant', content: 'Bonjour' })
    expect(store.currentAssistantContent).toBe('')
    expect(store.isStreaming).toBe(false)
  })

  it('OnAvatarEmotion_AfterAssistantMessage_ShouldAttachEmotion', () => {
    // arrange
    const store = useChatStore()
    store.initClient('ws://localhost:5000/ws')
    emittedHandlers.get('output:chat:chunk')!({ content: 'hi' })
    emittedHandlers.get('output:chat:end')!({})

    // act
    emittedHandlers.get('avatar:emotion')!({ emotion: 'joy' })

    // assert
    expect(store.messages[0]?.emotion).toBe('joy')
  })

  it('ClearMessages_ShouldResetMessagesAndBuffer', () => {
    // arrange
    const store = useChatStore()
    store.initClient('ws://localhost:5000/ws')
    emittedHandlers.get('output:chat:chunk')!({ content: 'hi' })
    emittedHandlers.get('output:chat:end')!({})

    // act
    store.clearMessages()

    // assert
    expect(store.messages).toHaveLength(0)
    expect(store.currentAssistantContent).toBe('')
  })

  it('OnError_WithFailedProvider_ShouldCaptureProviderInLastError', () => {
    // arrange
    const store = useChatStore()
    store.initClient('ws://localhost:5000/ws')

    // act
    emittedHandlers.get('error')!({
      message: 'Upstream timeout',
      code: 'stream_idle_timeout',
      category: 'permanent',
      failedProvider: 'ollama/gpt-oss-uncensored:latest',
    })

    // assert
    expect(store.lastError).toMatchObject({
      message: 'Upstream timeout',
      code: 'stream_idle_timeout',
      category: 'permanent',
      failedProvider: 'ollama/gpt-oss-uncensored:latest',
    })
  })

  it('RetryLastMessage_AfterError_ShouldClearErrorAndResendLastUserText', () => {
    // arrange
    const store = useChatStore()
    store.initClient('ws://localhost:5000/ws')
    // Push a user message manually (sendMessage would fail the test by
    // requiring a connected client; we're exercising the retry logic in
    // isolation here).
    store.messages.push({
      id: 'u1',
      role: 'user',
      content: 'hello',
      timestamp: 1,
    })
    store.messages.push({
      id: 'a1',
      role: 'assistant',
      content: 'broken response',
      timestamp: 2,
    })
    emittedHandlers.get('error')!({
      message: 'boom',
      category: 'transient',
    })
    expect(store.lastError).not.toBeNull()

    // act
    store.retryLastMessage()

    // assert
    expect(store.lastError).toBeNull()
    // retryLastMessage → sendMessage pushes a NEW user bubble for the
    // retried content. Now 3 messages: original user + assistant + new user.
    expect(store.messages).toHaveLength(3)
    expect(store.messages.at(-1)).toMatchObject({
      role: 'user',
      content: 'hello',
    })
  })

  it('RetryLastMessage_WithoutPriorUserMessage_ShouldBeNoOp', () => {
    // arrange
    const store = useChatStore()
    store.initClient('ws://localhost:5000/ws')
    expect(store.messages).toHaveLength(0)

    // act
    store.retryLastMessage()

    // assert — no crash, no message, no error change
    expect(store.messages).toHaveLength(0)
    expect(store.lastError).toBeNull()
  })
})
