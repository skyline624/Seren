import type { PluginListenerHandle } from '@capacitor/core'
import { Capacitor } from '@capacitor/core'
import type { WebSocketFactory } from '@seren/sdk'
import { HostWebSocket } from '@seren/capacitor-host-websocket'

/**
 * A `WebSocket`-compatible facade that, on native Capacitor platforms, routes
 * all traffic through the `HostWebSocket` plugin (URLSessionWebSocketTask on
 * iOS, OkHttp on Android). On web it stays out of the way and lets callers
 * use `new WebSocket(url)` directly.
 *
 * The class implements just enough of the DOM `WebSocket` interface for
 * `@seren/sdk`'s `Client` to consume it via the `webSocketFactory` option.
 */
export class HostWebSocketAdapter extends EventTarget {
  public static readonly CONNECTING = 0
  public static readonly OPEN = 1
  public static readonly CLOSING = 2
  public static readonly CLOSED = 3

  public readonly CONNECTING = HostWebSocketAdapter.CONNECTING
  public readonly OPEN = HostWebSocketAdapter.OPEN
  public readonly CLOSING = HostWebSocketAdapter.CLOSING
  public readonly CLOSED = HostWebSocketAdapter.CLOSED

  public readyState: number = HostWebSocketAdapter.CONNECTING
  public readonly url: string
  public readonly protocol = ''
  public readonly extensions = ''
  public readonly bufferedAmount = 0
  public binaryType: BinaryType = 'blob'

  public onopen: ((ev: Event) => void) | null = null
  public onmessage: ((ev: MessageEvent) => void) | null = null
  public onclose: ((ev: CloseEvent) => void) | null = null
  public onerror: ((ev: Event) => void) | null = null

  private readonly instanceId: string
  private readonly listenerHandles: Promise<PluginListenerHandle>[] = []

  constructor(url: string) {
    super()
    this.url = url
    this.instanceId = `seren-mobile-${crypto.randomUUID()}`
    this.attachListeners()
    void HostWebSocket.connect({ instanceId: this.instanceId, url }).catch((err: unknown) => {
      this.emitError(err instanceof Error ? err.message : String(err))
      this.emitClose(1006, 'connect failed', false)
    })
  }

  send(data: string | ArrayBufferLike | Blob | ArrayBufferView): void {
    if (this.readyState !== HostWebSocketAdapter.OPEN) {
      throw new Error(`HostWebSocketAdapter: cannot send while in readyState ${this.readyState}`)
    }

    if (typeof data === 'string') {
      void HostWebSocket.send({ instanceId: this.instanceId, data })
      return
    }

    const buffer = data instanceof Blob
      ? null
      : ArrayBuffer.isView(data)
        ? data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength)
        : (data as ArrayBuffer)

    if (!buffer) {
      throw new Error('HostWebSocketAdapter: Blob send not supported in bridge yet')
    }

    const bytes = new Uint8Array(buffer)
    let binary = ''
    for (let i = 0; i < bytes.length; i++) {
      binary += String.fromCharCode(bytes[i]!)
    }
    const dataBase64 = btoa(binary)
    void HostWebSocket.sendBinary({ instanceId: this.instanceId, dataBase64 })
  }

  close(code = 1000, reason = ''): void {
    if (this.readyState === HostWebSocketAdapter.CLOSED || this.readyState === HostWebSocketAdapter.CLOSING) {
      return
    }
    this.readyState = HostWebSocketAdapter.CLOSING
    void HostWebSocket.close({ instanceId: this.instanceId, code, reason })
  }

  // ── Listener wiring ───────────────────────────────────────────────────

  private attachListeners(): void {
    this.listenerHandles.push(
      HostWebSocket.addListener('onOpen', (event) => {
        if (event.instanceId !== this.instanceId) return
        this.readyState = HostWebSocketAdapter.OPEN
        const ev = new Event('open')
        this.onopen?.(ev)
        this.dispatchEvent(ev)
      }),
    )

    this.listenerHandles.push(
      HostWebSocket.addListener('onMessage', (event) => {
        if (event.instanceId !== this.instanceId) return
        const payload = event.data ?? (event.dataBase64 ? atob(event.dataBase64) : '')
        const ev = new MessageEvent('message', { data: payload })
        this.onmessage?.(ev)
        this.dispatchEvent(ev)
      }),
    )

    this.listenerHandles.push(
      HostWebSocket.addListener('onClose', (event) => {
        if (event.instanceId !== this.instanceId) return
        this.emitClose(event.code, event.reason, event.wasClean)
      }),
    )

    this.listenerHandles.push(
      HostWebSocket.addListener('onError', (event) => {
        if (event.instanceId !== this.instanceId) return
        this.emitError(event.message)
      }),
    )
  }

  private emitError(message: string): void {
    const ev = new Event('error')
    Object.defineProperty(ev, 'message', { value: message })
    this.onerror?.(ev)
    this.dispatchEvent(ev)
  }

  private emitClose(code: number, reason: string, wasClean: boolean): void {
    this.readyState = HostWebSocketAdapter.CLOSED
    const ev = new CloseEvent('close', { code, reason, wasClean })
    this.onclose?.(ev)
    this.dispatchEvent(ev)
    void this.detachListeners()
  }

  private async detachListeners(): Promise<void> {
    const handles = await Promise.all(this.listenerHandles)
    for (const handle of handles) {
      try {
        await handle.remove()
      } catch {
        // best-effort
      }
    }
  }
}

/**
 * Factory selected at runtime: native bridge on Capacitor, plain WebSocket on
 * web. Pass this to the `@seren/sdk` Client options.
 */
export const mobileWebSocketFactory: WebSocketFactory = (url: string) => {
  if (Capacitor.isNativePlatform()) {
    return new HostWebSocketAdapter(url) as unknown as WebSocket
  }
  return new WebSocket(url)
}
