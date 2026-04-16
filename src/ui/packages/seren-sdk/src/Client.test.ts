import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { Client } from './Client'
import { EventTypes } from './types/event-types'
import { Server, type Client as MockSocket } from 'mock-socket'
import type { WebSocketEnvelope, AnnouncedPayload, HeartbeatPayload } from './types/events'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const WS_URL = 'ws://localhost:42195'

function createClient(overrides: { token?: string; pingInterval?: number; readTimeout?: number; maxBackoffSeconds?: number } = {}) {
  return new Client({
    url: WS_URL,
    token: overrides.token,
    pingInterval: overrides.pingInterval ?? 15_000,
    readTimeout: overrides.readTimeout ?? 30_000,
    maxBackoffSeconds: overrides.maxBackoffSeconds ?? 30,
  })
}

function waitForConnection(server: Server): Promise<MockSocket> {
  return new Promise((resolve) => {
    server.on('connection', (socket) => resolve(socket))
  })
}

function collectMessages(socket: MockSocket): string[] {
  const messages: string[] = []
  socket.on('message', (raw) => {
    messages.push(raw as string)
  })
  return messages
}

function parseEnvelope(raw: string): WebSocketEnvelope {
  return JSON.parse(raw) as WebSocketEnvelope
}

function announcedEnvelope(): WebSocketEnvelope<AnnouncedPayload> {
  return {
    type: EventTypes.ModuleAnnounced,
    data: { identity: { id: 'server', pluginId: 'seren-hub' }, name: 'test', index: 0 },
    metadata: { source: { id: 'server', pluginId: 'seren-hub' }, event: { id: '1' } },
  }
}

// ---------------------------------------------------------------------------
// Tests with real timers
// ---------------------------------------------------------------------------

describe('Client (real timers)', () => {
  let server: Server

  beforeEach(() => {
    server = new Server(WS_URL)
  })

  afterEach(() => {
    server.stop()
  })

  // ── connect ──────────────────────────────────────────────────────────

  it('transitions idle -> ready when no token is required', async () => {
    const client = createClient()

    const socketPromise = waitForConnection(server)
    client.connect()

    const socket = await socketPromise

    // Server responds with module:announced
    socket.send(JSON.stringify(announcedEnvelope()))

    await new Promise((r) => setTimeout(r, 10))

    expect(client.currentStatus).toBe('ready')
  })

  // ── authentication ───────────────────────────────────────────────────

  it('sends module:authenticate before announce when a token is provided', async () => {
    const client = createClient({ token: 'secret' })
    const socketPromise = waitForConnection(server)
    client.connect()

    const socket = await socketPromise
    const messages = collectMessages(socket)

    await new Promise((r) => setTimeout(r, 10))

    const authMsg = messages.find((m) => {
      const env = parseEnvelope(m)
      return env.type === EventTypes.ModuleAuthenticate
    })
    expect(authMsg).toBeDefined()

    const authEnvelope = parseEnvelope(authMsg!)
    expect(authEnvelope.data).toEqual({ token: 'secret' })
  })

  // ── send ──────────────────────────────────────────────────────────────

  it('sends a JSON envelope via WebSocket', async () => {
    const client = createClient()
    const socketPromise = waitForConnection(server)
    client.connect()

    const socket = await socketPromise
    const messages = collectMessages(socket)

    await new Promise((r) => setTimeout(r, 10))

    const announceMsg = messages.find((m) => {
      const env = parseEnvelope(m)
      return env.type === EventTypes.ModuleAnnounce
    })
    expect(announceMsg).toBeDefined()

    const envelope = parseEnvelope(announceMsg!)
    expect(envelope.type).toBe(EventTypes.ModuleAnnounce)
    expect(envelope.metadata).toBeDefined()
    expect(envelope.metadata.event.id).toBeDefined()
  })

  // ── onEvent ───────────────────────────────────────────────────────────

  it('dispatches events to registered handlers', async () => {
    const client = createClient()
    const socketPromise = waitForConnection(server)
    client.connect()

    const socket = await socketPromise

    const received: string[] = []
    client.onEvent(EventTypes.OutputChatChunk, (data) => {
      received.push((data as { content: string }).content)
    })

    const chunk = {
      type: EventTypes.OutputChatChunk,
      data: { content: 'Hello' },
      metadata: { source: { id: 's', pluginId: 's' }, event: { id: '2' } },
    }
    socket.send(JSON.stringify(chunk))

    await new Promise((r) => setTimeout(r, 10))

    expect(received).toEqual(['Hello'])
  })

  // ── offEvent ──────────────────────────────────────────────────────────

  it('stops calling handler after offEvent', async () => {
    const client = createClient()
    const socketPromise = waitForConnection(server)
    client.connect()

    const socket = await socketPromise

    const received: string[] = []
    const handler = (data: unknown) => {
      received.push((data as { content: string }).content)
    }

    const unsub = client.onEvent(EventTypes.OutputChatChunk, handler)

    const chunk1 = {
      type: EventTypes.OutputChatChunk,
      data: { content: 'A' },
      metadata: { source: { id: 's', pluginId: 's' }, event: { id: '3' } },
    }
    socket.send(JSON.stringify(chunk1))
    await new Promise((r) => setTimeout(r, 10))

    unsub()

    const chunk2 = {
      type: EventTypes.OutputChatChunk,
      data: { content: 'B' },
      metadata: { source: { id: 's', pluginId: 's' }, event: { id: '4' } },
    }
    socket.send(JSON.stringify(chunk2))
    await new Promise((r) => setTimeout(r, 10))

    expect(received).toEqual(['A'])
  })

  // ── disconnect ───────────────────────────────────────────────────────

  it('cleans up and resets status to idle on disconnect', async () => {
    const client = createClient()
    const socketPromise = waitForConnection(server)
    client.connect()

    const socket = await socketPromise
    socket.send(JSON.stringify(announcedEnvelope()))
    await new Promise((r) => setTimeout(r, 10))

    expect(client.currentStatus).toBe('ready')

    client.disconnect()
    expect(client.currentStatus).toBe('idle')
  })

  // ── send throws when idle ────────────────────────────────────────────

  it('throws when sending while idle', () => {
    const client = createClient()

    expect(() => client.send('test', {})).toThrow('Cannot send in status "idle"')
  })

  // ── send throws when closing ─────────────────────────────────────────

  it('throws when sending while closing', async () => {
    const client = createClient()
    const socketPromise = waitForConnection(server)
    client.connect()

    const socket = await socketPromise
    socket.send(JSON.stringify(announcedEnvelope()))
    await new Promise((r) => setTimeout(r, 10))

    client.disconnect()

    expect(() => client.send('test', {})).toThrow('Cannot send in status')
  })

  // ── heartbeat with real timers ───────────────────────────────────────

  it('sends heartbeat messages at the configured interval', async () => {
    const client = createClient({ pingInterval: 100 })
    const socketPromise = waitForConnection(server)
    client.connect()

    const socket = await socketPromise
    const messages = collectMessages(socket)

    // Wait for two heartbeat intervals
    await new Promise((r) => setTimeout(r, 250))

    const heartbeatMessages = messages.filter((m) => {
      const env = parseEnvelope(m)
      return env.type === EventTypes.TransportHeartbeat
    })

    expect(heartbeatMessages.length).toBeGreaterThanOrEqual(2)

    const hb = parseEnvelope(heartbeatMessages[0]!) as WebSocketEnvelope<HeartbeatPayload>
    expect(hb.data.kind).toBe('ping')
  })

  // ── reconnect ────────────────────────────────────────────────────────

  it('attempts reconnect after server closes the connection', async () => {
    const client = createClient({ pingInterval: 60_000, maxBackoffSeconds: 2 })
    const socketPromise = waitForConnection(server)
    client.connect()

    const socket = await socketPromise
    await new Promise((r) => setTimeout(r, 20))

    // Close the server-side socket, which triggers the client's close event
    socket.close()

    // After close, the client schedules a reconnect. First backoff is 1s (2^0).
    // Stop the old server before the reconnect fires
    server.stop()

    // Create a fresh server to accept the reconnection
    const reconnectServer = new Server(WS_URL)
    const reconnectPromise = waitForConnection(reconnectServer)

    // Wait up to 4 seconds for the reconnect (first backoff is 1s)
    const reconnected = await Promise.race([
      reconnectPromise.then(() => true),
      new Promise<boolean>((r) => setTimeout(() => r(false), 4000)),
    ])

    expect(reconnected).toBe(true)

    reconnectServer.stop()
  })
})