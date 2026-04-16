import { WebPlugin } from '@capacitor/core'
import type {
  HostWebSocketPlugin,
} from './definitions'

/**
 * Web fallback — routes through the standard browser `WebSocket` so that
 * `pnpm dev` on desktop still works without the native plugin. Keeps the
 * same event surface so app code stays uniform.
 */
export class HostWebSocketWeb extends WebPlugin implements HostWebSocketPlugin {
  private readonly sockets = new Map<string, WebSocket>()

  async connect(options: { instanceId: string, url: string, protocols?: string[] }): Promise<void> {
    if (this.sockets.has(options.instanceId)) {
      throw new Error(`HostWebSocket: instance "${options.instanceId}" is already connected`)
    }

    const ws = new WebSocket(options.url, options.protocols)
    this.sockets.set(options.instanceId, ws)

    ws.addEventListener('open', () => {
      this.notifyListeners('onOpen', { instanceId: options.instanceId })
    })

    ws.addEventListener('message', (ev: MessageEvent) => {
      const payload = typeof ev.data === 'string' ? { data: ev.data } : { data: String(ev.data) }
      this.notifyListeners('onMessage', { instanceId: options.instanceId, ...payload })
    })

    ws.addEventListener('close', (ev: CloseEvent) => {
      this.notifyListeners('onClose', {
        instanceId: options.instanceId,
        code: ev.code,
        reason: ev.reason,
        wasClean: ev.wasClean,
      })
      this.sockets.delete(options.instanceId)
    })

    ws.addEventListener('error', () => {
      this.notifyListeners('onError', {
        instanceId: options.instanceId,
        message: 'WebSocket error',
      })
    })
  }

  async send(options: { instanceId: string, data: string }): Promise<void> {
    const ws = this.sockets.get(options.instanceId)
    if (!ws)
      throw new Error(`HostWebSocket: unknown instance "${options.instanceId}"`)
    ws.send(options.data)
  }

  async sendBinary(options: { instanceId: string, dataBase64: string }): Promise<void> {
    const ws = this.sockets.get(options.instanceId)
    if (!ws)
      throw new Error(`HostWebSocket: unknown instance "${options.instanceId}"`)
    const binary = atob(options.dataBase64)
    const bytes = new Uint8Array(binary.length)
    for (let i = 0; i < binary.length; i++)
      bytes[i] = binary.charCodeAt(i)
    ws.send(bytes.buffer)
  }

  async close(options: { instanceId: string, code?: number, reason?: string }): Promise<void> {
    const ws = this.sockets.get(options.instanceId)
    if (!ws) return
    ws.close(options.code ?? 1000, options.reason)
    this.sockets.delete(options.instanceId)
  }
}
