import type { PluginListenerHandle } from '@capacitor/core'

/**
 * Events emitted by the native host for each managed WebSocket instance.
 *
 * The JS side wraps these into a `WebSocket`-compatible object so that
 * `@seren/sdk` can consume them without knowing about Capacitor.
 */
export interface HostWebSocketOpenEvent {
  instanceId: string
}

export interface HostWebSocketMessageEvent {
  instanceId: string
  /** Text frame payload. Binary is encoded as base64 and sent as `dataBase64`. */
  data?: string
  dataBase64?: string
}

export interface HostWebSocketCloseEvent {
  instanceId: string
  code: number
  reason: string
  wasClean: boolean
}

export interface HostWebSocketErrorEvent {
  instanceId: string
  message: string
}

export interface HostWebSocketPlugin {
  /** Open a new WebSocket connection. Must be unique per instanceId. */
  connect(options: {
    instanceId: string
    url: string
    protocols?: string[]
    headers?: Record<string, string>
  }): Promise<void>

  /** Send a UTF-8 text frame. */
  send(options: { instanceId: string, data: string }): Promise<void>

  /** Send a binary frame (base64-encoded). */
  sendBinary(options: { instanceId: string, dataBase64: string }): Promise<void>

  /** Close the connection with an optional code and reason. */
  close(options: { instanceId: string, code?: number, reason?: string }): Promise<void>

  addListener(
    eventName: 'onOpen',
    listener: (event: HostWebSocketOpenEvent) => void,
  ): Promise<PluginListenerHandle>

  addListener(
    eventName: 'onMessage',
    listener: (event: HostWebSocketMessageEvent) => void,
  ): Promise<PluginListenerHandle>

  addListener(
    eventName: 'onClose',
    listener: (event: HostWebSocketCloseEvent) => void,
  ): Promise<PluginListenerHandle>

  addListener(
    eventName: 'onError',
    listener: (event: HostWebSocketErrorEvent) => void,
  ): Promise<PluginListenerHandle>

  removeAllListeners(): Promise<void>
}
