import type {
  AnnouncePayload,
  EventMetadata,
  ModuleIdentityDto,
  WebSocketEnvelope,
} from './types/events'
import { EventTypes, type EventTypeName } from './types/event-types'

// ── Public types ────────────────────────────────────────────────────────────

export type ClientStatus =
  | 'idle'
  | 'connecting'
  | 'authenticating'
  | 'announcing'
  | 'ready'
  | 'closing'

/**
 * Abstraction over the browser `WebSocket` constructor. Allows injecting a
 * native Capacitor bridge on mobile where WebViews have reliability issues.
 *
 * Any implementation must match the WebSocket DOM interface: an `open`,
 * `message`, `close` and `error` event, a `send(data)` method, a `close(code?,
 * reason?)` method and a `readyState` property.
 */
export type WebSocketFactory = (url: string) => WebSocket

export interface ClientOptions {
  url: string
  token?: string
  /** Heartbeat interval in ms (default 15 000). */
  pingInterval?: number
  /** Time without any received message before considering the connection dead in ms (default 30 000). */
  readTimeout?: number
  /** Max reconnect backoff in seconds (default 30). */
  maxBackoffSeconds?: number
  /**
   * Custom factory producing a WebSocket-compatible object. Defaults to
   * `new WebSocket(url)`. Use this to route through a native bridge.
   */
  webSocketFactory?: WebSocketFactory
}

// ── Internal types ──────────────────────────────────────────────────────────

type EventHandler<T = unknown> = (data: T, envelope: WebSocketEnvelope<T>) => void

interface ResolvedClientOptions {
  readonly url: string
  readonly token: string
  readonly pingInterval: number
  readonly readTimeout: number
  readonly maxBackoffSeconds: number
  readonly webSocketFactory: WebSocketFactory
}

const defaultWebSocketFactory: WebSocketFactory = (url) => new WebSocket(url)

// ── Client ─────────────────────────────────────────────────────────────────

export class Client {
  private ws: WebSocket | null = null
  private status: ClientStatus = 'idle'
  private readonly listeners = new Map<string, Set<EventHandler>>()
  private reconnectAttempts = 0
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null
  private heartbeatTimer: ReturnType<typeof setInterval> | null = null
  private readTimeoutTimer: ReturnType<typeof setTimeout> | null = null
  private readonly options: ResolvedClientOptions
  private readonly identity: ModuleIdentityDto

  constructor(options: ClientOptions) {
    this.options = {
      url: options.url,
      token: options.token ?? '',
      pingInterval: options.pingInterval ?? 15_000,
      readTimeout: options.readTimeout ?? 30_000,
      maxBackoffSeconds: options.maxBackoffSeconds ?? 30,
      webSocketFactory: options.webSocketFactory ?? defaultWebSocketFactory,
    }

    this.identity = {
      id: crypto.randomUUID(),
      pluginId: 'seren-sdk',
    }
  }

  get currentStatus(): ClientStatus {
    return this.status
  }

  // ── Connection lifecycle ───────────────────────────────────────────────

  connect(): void {
    if (this.status !== 'idle') return

    this.setStatus('connecting')

    const ws = this.options.webSocketFactory(this.options.url)
    this.ws = ws

    ws.addEventListener('open', this.handleOpen)
    ws.addEventListener('message', this.handleMessage)
    ws.addEventListener('close', this.handleClose)
    ws.addEventListener('error', this.handleError)
  }

  disconnect(): void {
    this.setStatus('closing')

    this.clearReconnect()
    this.stopHeartbeat()

    if (this.ws) {
      this.ws.removeEventListener('open', this.handleOpen)
      this.ws.removeEventListener('message', this.handleMessage)
      this.ws.removeEventListener('close', this.handleClose)
      this.ws.removeEventListener('error', this.handleError)
      this.ws.close(1000, 'client disconnect')
      this.ws = null
    }

    this.setStatus('idle')
  }

  // ── Sending ────────────────────────────────────────────────────────────

  send<T>(type: string, data: T, metadata?: Partial<EventMetadata>): void {
    if (this.status === 'idle' || this.status === 'closing') {
      throw new Error(`Cannot send in status "${this.status}"`)
    }

    const envelope = this.buildEnvelope(type, data, metadata)
    this.ws?.send(JSON.stringify(envelope))
  }

  // ── Event subscription ─────────────────────────────────────────────────

  onEvent<T = unknown>(type: EventTypeName | string, handler: EventHandler<T>): () => void {
    let set = this.listeners.get(type)
    if (!set) {
      set = new Set()
      this.listeners.set(type, set)
    }
    set.add(handler as EventHandler)

    return () => this.offEvent(type, handler)
  }

  offEvent<T = unknown>(type: string, handler: EventHandler<T>): void {
    this.listeners.get(type)?.delete(handler as EventHandler)
  }

  // ── WebSocket event handlers (arrow functions to preserve `this`) ──────

  private handleOpen = (): void => {
    this.reconnectAttempts = 0

    if (this.options.token) {
      this.send(EventTypes.ModuleAuthenticate, { token: this.options.token })
      this.setStatus('authenticating')
    } else {
      this.announce()
      this.setStatus('announcing')
    }

    this.startHeartbeat()
  }

  private handleMessage = (raw: MessageEvent): void => {
    this.resetReadTimeout()

    let envelope: WebSocketEnvelope
    try {
      envelope = JSON.parse(raw.data as string) as WebSocketEnvelope
    } catch {
      return
    }

    const { type, data } = envelope
    const handlers = this.listeners.get(type)
    if (handlers) {
      for (const handler of handlers) {
        handler(data, envelope)
      }
    }

    // Status transitions driven by server responses
    if (type === EventTypes.ModuleAuthenticated && this.status === 'authenticating') {
      this.announce()
      this.setStatus('announcing')
    } else if (type === EventTypes.ModuleAnnounced && (this.status === 'announcing' || this.status === 'authenticating')) {
      this.setStatus('ready')
    }
  }

  private handleClose = (): void => {
    this.cleanupConnection()
    this.setStatus('idle')
    this.attemptReconnect()
  }

  private handleError = (): void => {
    this.ws?.close()
  }

  // ── Heartbeat ──────────────────────────────────────────────────────────

  private startHeartbeat(): void {
    this.stopHeartbeat()
    this.heartbeatTimer = setInterval(() => {
      if (this.ws?.readyState === WebSocket.OPEN) {
        this.send(EventTypes.TransportHeartbeat, { kind: 'ping', at: Date.now() })
      }
    }, this.options.pingInterval)

    this.resetReadTimeout()
  }

  private stopHeartbeat(): void {
    if (this.heartbeatTimer !== null) {
      clearInterval(this.heartbeatTimer)
      this.heartbeatTimer = null
    }
    this.clearReadTimeout()
  }

  private resetReadTimeout(): void {
    this.clearReadTimeout()
    this.readTimeoutTimer = setTimeout(() => {
      this.ws?.close(4000, 'read timeout')
    }, this.options.readTimeout)
  }

  private clearReadTimeout(): void {
    if (this.readTimeoutTimer !== null) {
      clearTimeout(this.readTimeoutTimer)
      this.readTimeoutTimer = null
    }
  }

  // ── Reconnection ───────────────────────────────────────────────────────

  private attemptReconnect(): void {
    if (this.status === 'closing') return

    const delay = Math.min(2 ** this.reconnectAttempts, this.options.maxBackoffSeconds) * 1000
    this.reconnectAttempts += 1

    this.reconnectTimer = setTimeout(() => {
      this.connect()
    }, delay)
  }

  private clearReconnect(): void {
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
    this.reconnectAttempts = 0
  }

  // ── Helpers ────────────────────────────────────────────────────────────

  private announce(): void {
    const payload: AnnouncePayload = {
      identity: this.identity,
      name: 'seren-sdk-client',
    }
    this.send(EventTypes.ModuleAnnounce, payload)
  }

  private cleanupConnection(): void {
    this.stopHeartbeat()
    if (this.ws) {
      this.ws.removeEventListener('open', this.handleOpen)
      this.ws.removeEventListener('message', this.handleMessage)
      this.ws.removeEventListener('close', this.handleClose)
      this.ws.removeEventListener('error', this.handleError)
      this.ws = null
    }
  }

  private setStatus(status: ClientStatus): void {
    this.status = status
  }

  private buildEnvelope<T>(
    type: string,
    data: T,
    partial?: Partial<EventMetadata>,
  ): WebSocketEnvelope<T> {
    const metadata: EventMetadata = {
      source: partial?.source ?? this.identity,
      event: {
        id: crypto.randomUUID(),
        ...partial?.event,
      },
    }

    return { type, data, metadata }
  }
}