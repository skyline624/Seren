import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

// Mock @seren/sdk (same pattern as chat.test.ts) so the chat store can
// boot without a real WebSocket.
vi.mock('@seren/sdk', () => {
  class FakeClient {
    public currentStatus = 'idle' as string
    constructor(_: unknown) { }
    onEvent(_t: string, _h: (d: unknown) => void): () => void { return () => { } }
    send(_t: string, _d: unknown): void { }
    connect(): void { this.currentStatus = 'ready' }
    disconnect(): void { this.currentStatus = 'idle' }
  }
  return {
    Client: FakeClient,
    EventTypes: { Error: 'error' },
    generateId: () => 'test-id',
  }
})

const { useChatStore } = await import('./chat')
const { useAvatarStateStore, PHASE_GAINS } = await import('./avatarState')

describe('useAvatarStateStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('InitialState_WithoutConnection_IsIdle', () => {
    const avatar = useAvatarStateStore()
    expect(avatar.phase).toBe('idle')
  })

  it('ConnectionReady_WithNoActivity_IsListening', () => {
    const chat = useChatStore()
    chat.connectionStatus = 'ready'
    const avatar = useAvatarStateStore()
    expect(avatar.phase).toBe('listening')
  })

  it('IsStreaming_Flag_BeatsEverythingElse_ReturnsTalking', () => {
    const chat = useChatStore()
    chat.connectionStatus = 'ready'
    chat.isThinking = true
    chat.isStreaming = true
    const avatar = useAvatarStateStore()
    expect(avatar.phase).toBe('talking')
  })

  it('IsSpeaking_AfterStreamEnd_StaysTalking', () => {
    const chat = useChatStore()
    chat.connectionStatus = 'ready'
    chat.isStreaming = false
    chat.isSpeaking = true
    const avatar = useAvatarStateStore()
    expect(avatar.phase).toBe('talking')
  })

  it('IsThinking_WithoutStreaming_IsThinking', () => {
    const chat = useChatStore()
    chat.connectionStatus = 'ready'
    chat.isThinking = true
    const avatar = useAvatarStateStore()
    expect(avatar.phase).toBe('thinking')
  })

  it('CurrentAction_Set_AndIdleOtherwise_IsReactive', () => {
    const chat = useChatStore()
    chat.connectionStatus = 'ready'
    chat.currentAction = { action: 'wave', nonce: 1 }
    const avatar = useAvatarStateStore()
    expect(avatar.phase).toBe('reactive')
  })

  it('PriorityOrder_TalkingBeatsThinking', () => {
    const chat = useChatStore()
    chat.connectionStatus = 'ready'
    chat.isThinking = true
    chat.isStreaming = true
    const avatar = useAvatarStateStore()
    expect(avatar.phase).toBe('talking')
  })

  it('PriorityOrder_ThinkingBeatsReactive', () => {
    const chat = useChatStore()
    chat.connectionStatus = 'ready'
    chat.isThinking = true
    chat.currentAction = { action: 'nod', nonce: 1 }
    const avatar = useAvatarStateStore()
    expect(avatar.phase).toBe('thinking')
  })

  it('Gains_IdlePhase_AreIdentity', () => {
    const avatar = useAvatarStateStore()
    expect(avatar.gains).toEqual(PHASE_GAINS.idle)
    expect(avatar.gains.bodySway).toBe(1)
    expect(avatar.gains.blink).toBe(1)
    expect(avatar.gains.saccade).toBe(1)
    expect(avatar.gains.headTilt).toBe(0)
  })

  it('Gains_ThinkingPhase_AppliesNegativeHeadTilt', () => {
    const chat = useChatStore()
    chat.connectionStatus = 'ready'
    chat.isThinking = true
    const avatar = useAvatarStateStore()
    expect(avatar.phase).toBe('thinking')
    expect(avatar.gains.headTilt).toBeLessThan(0)
    expect(avatar.gains.bodySway).toBeLessThan(1)  // less sway = more focused
  })

  it('PhaseGainsTable_HasEntryForEveryPhase', () => {
    const phases = ['idle', 'listening', 'thinking', 'talking', 'reactive'] as const
    for (const phase of phases) {
      expect(PHASE_GAINS[phase]).toBeDefined()
      expect(PHASE_GAINS[phase].bodySway).toBeGreaterThan(0)
    }
  })
})
