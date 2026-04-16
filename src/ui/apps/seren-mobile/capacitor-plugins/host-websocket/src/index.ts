import { registerPlugin } from '@capacitor/core'
import type { HostWebSocketPlugin } from './definitions'

const HostWebSocket = registerPlugin<HostWebSocketPlugin>('HostWebSocket', {
  web: async () => {
    const { HostWebSocketWeb } = await import('./web')
    return new HostWebSocketWeb()
  },
})

export * from './definitions'
export { HostWebSocket }
