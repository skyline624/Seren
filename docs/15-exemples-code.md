# 15 — Exemples de code

Ce document rassemble des exemples de code **prêts à l'emploi** pour chaque couche du projet AIRI. Ces snippets peuvent être copiés-collés dans un projet reproduit pour valider la compréhension de l'architecture.

## 15.1 Utilisation du SDK serveur (`@proj-airi/server-sdk`)

### 15.1.1 Client minimal

```typescript
import { Client } from '@proj-airi/server-sdk'

const client = new Client({
  name: 'my-first-module',
  url: 'ws://localhost:6121/ws',
  possibleEvents: ['input:text', 'output:gen-ai:chat:message'],
  onReady: () => console.log('Ready!'),
  onError: (err) => console.error('Error:', err),
})

// Envoyer un message
client.send({
  type: 'input:text',
  data: {
    text: 'Hello AIRI!',
  },
})

// Écouter les réponses
client.onEvent('output:gen-ai:chat:message', (event) => {
  console.log('Response:', event.data.message.content)
})
```

### 15.1.2 Client avec auth token et reconnexion custom

```typescript
import { Client } from '@proj-airi/server-sdk'

const client = new Client({
  name: 'authenticated-module',
  url: process.env.AIRI_URL!,
  token: process.env.AIRI_TOKEN!,
  possibleEvents: ['input:text', 'module:configure'],
  heartbeat: {
    pingInterval: 10_000,
    readTimeout: 30_000,
  },
  autoReconnect: true,
  maxReconnectAttempts: 20,
  onStateChange: ({ status, error }) => {
    console.log(`State: ${status}`, error ? `Error: ${error.message}` : '')
  },
})

// Attendre que le client soit prêt (en cas d'erreur d'auth, throw)
try {
  await client.ready({ timeout: 10_000 })
  console.log('Connected and announced')
}
catch (e) {
  console.error('Failed to connect:', e)
  process.exit(1)
}
```

### 15.1.3 Sélecteur de destination (route)

```typescript
// Envoyer un message à un groupe précis de modules stage-* en prod
client.send({
  type: 'output:gen-ai:chat:message',
  data: { message: { role: 'assistant', content: 'Hello' } },
  route: {
    destinations: [
      {
        type: 'and',
        all: [
          { type: 'glob', glob: 'proj-airi:stage-*' },
          { type: 'label', selectors: ['env=prod'] },
        ],
      },
    ],
  },
})
```

### 15.1.4 Consumer group (load balancing)

```typescript
// Ce plugin s'enregistre comme consumer du pool "llm-workers"
// Plusieurs instances reçoivent les événements en round-robin.
client.send({
  type: 'module:consumer:register',
  data: {
    event: 'input:text',
    group: 'llm-workers',
    priority: 10,
    selection: 'round-robin',
  },
})

client.onEvent('input:text', async (event) => {
  const result = await processWithLLM(event.data.text)
  client.send({
    type: 'output:gen-ai:chat:message',
    data: { message: { role: 'assistant', content: result } },
  })
})
```

---

## 15.2 Contributions capabilities (plugin SDK)

### 15.2.1 Plugin minimal

```typescript
// plugins/my-plugin/src/index.ts
import { definePlugin } from '@proj-airi/plugin-sdk'

export default definePlugin({
  id: 'my-plugin',
  version: '0.1.0',
  dependencies: [],
  configSchema: {
    id: 'my-plugin.config',
    version: 1,
    schema: {
      type: 'object',
      required: ['greeting'],
      properties: {
        greeting: { type: 'string' },
      },
    },
  },

  async setup({ client, config, log }) {
    log.info('my-plugin starting with greeting:', config.greeting)

    const unsubscribe = client.onEvent('input:text', (event) => {
      if (event.data.text.toLowerCase().includes('/hello')) {
        client.send({
          type: 'output:gen-ai:chat:message',
          data: {
            message: {
              role: 'assistant',
              content: `${config.greeting}, ${event.metadata.source.id}!`,
            },
          },
        })
      }
    })

    return { unsubscribe }
  },

  async teardown({ state }) {
    state?.unsubscribe?.()
  },
})
```

### 15.2.2 Contribute capability

```typescript
// Offrir une capability que d'autres modules peuvent utiliser
client.send({
  type: 'module:contribute:capability:offer',
  data: {
    identity: clientIdentity,
    capability: {
      id: 'my-plugin.weather-lookup',
      name: 'Weather Lookup',
      description: { key: 'capability.weather.description', fallback: 'Look up weather for a city' },
      configSchema: {
        id: 'my-plugin.weather-lookup.config',
        version: 1,
        schema: {
          type: 'object',
          required: ['apiKey'],
          properties: { apiKey: { type: 'string' } },
        },
      },
    },
  },
})
```

---

## 15.3 Server runtime (embarqué dans un service Node)

### 15.3.1 Démarrer le runtime

```typescript
// server-runtime-launcher.ts
import { createServer } from '@proj-airi/server-runtime/server'

const server = await createServer({
  port: 6121,
  token: process.env.AIRI_TOKEN,  // optionnel
  cors: {
    allowedOrigins: ['https://airi.moeru.ai', 'http://localhost:5173'],
  },
  rateLimit: {
    max: 100,
    windowMs: 10_000,
  },
  logLevel: 'info',
})

console.log(`Server listening on port ${server.port}`)

process.on('SIGINT', async () => {
  await server.close()
  process.exit(0)
})
```

### 15.3.2 Ajouter un middleware de routing custom

```typescript
// server-runtime-with-policy.ts
import { setupApp } from '@proj-airi/server-runtime'
import type { RouteMiddleware } from '@proj-airi/server-runtime'

const blockDevtools: RouteMiddleware = (ctx, next) => {
  // Refuser tous les events vers des peers devtools en prod
  if (process.env.NODE_ENV === 'production') {
    const targets = next()
    if (typeof targets === 'object' && 'targets' in targets) {
      targets.targets = targets.targets.filter(p => !p.identity?.labels?.devtools)
    }
    return targets
  }
  return next()
}

const app = setupApp({
  middlewares: [blockDevtools],
})

export { app }
```

---

## 15.4 Electron main process

### 15.4.1 Bootstrap minimal avec injeca

```typescript
// apps/my-electron-app/src/main/index.ts
import { app } from 'electron'
import { injeca } from 'injeca'
import { createGlobalAppConfig } from './configs/global'
import { setupMainWindow } from './windows/main'
import { setupServerChannel } from './services/airi/channel-server'

app.whenReady().then(async () => {
  const appConfig = injeca.provide('configs:app', () => createGlobalAppConfig())

  const serverChannel = injeca.provide('server:channel', {
    dependsOn: { app: injeca.provide('electron:app', () => app) },
    build: ({ dependsOn }) => setupServerChannel(dependsOn),
  })

  const mainWindow = injeca.provide('windows:main', {
    dependsOn: { serverChannel, appConfig },
    build: ({ dependsOn }) => setupMainWindow(dependsOn),
  })

  injeca.invoke({
    dependsOn: { mainWindow, serverChannel },
    callback: () => console.log('All services ready'),
  })

  await injeca.start()
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})
```

### 15.4.2 Eventa contract + main handler

```typescript
// src/shared/eventa.ts
import { defineInvokeEventa } from '@moeru/eventa'

export const getBatteryLevel = defineInvokeEventa<
  { level: number, charging: boolean },  // Response
  void                                    // No payload
>('system.battery.level')
```

```typescript
// src/main/services/battery.ts
import { defineInvokeHandler } from '@moeru/eventa/electron-main'
import { powerMonitor } from 'electron'
import { getBatteryLevel } from '../../shared/eventa'

export function setupBatteryService() {
  defineInvokeHandler(getBatteryLevel, async () => ({
    level: powerMonitor.getSystemIdleTime(), // placeholder
    charging: !powerMonitor.isOnBatteryPower(),
  }))
}
```

```vue
<!-- src/renderer/components/BatteryIndicator.vue -->
<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { invoke } from '@moeru/eventa/electron-renderer'
import { getBatteryLevel } from '../../shared/eventa'

const level = ref(0)
const charging = ref(false)

onMounted(async () => {
  const data = await invoke(getBatteryLevel, undefined)
  level.value = data.level
  charging.value = data.charging
})
</script>

<template>
  <div>{{ level }}% {{ charging ? '(charging)' : '' }}</div>
</template>
```

---

## 15.5 Stores Pinia pour providers LLM

### 15.5.1 Provider OpenAI standardisé

```typescript
// packages/stage-ui/src/stores/providers/openai/index.ts
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { createOpenAI } from '@xsai-ext/providers'

export const useOpenAIProviderStore = defineStore('provider-openai', () => {
  const apiKey = ref<string>('')
  const baseURL = ref<string>('https://api.openai.com/v1')
  const model = ref<string>('gpt-4o')

  const isConfigured = computed(() => apiKey.value.length > 0)

  const client = computed(() => isConfigured.value
    ? createOpenAI({ apiKey: apiKey.value, baseURL: baseURL.value })
    : null)

  return { apiKey, baseURL, model, isConfigured, client }
}, {
  persist: true,
})
```

### 15.5.2 Module consciousness (appel LLM avec streaming)

```typescript
// packages/stage-ui/src/stores/modules/consciousness.ts
import { defineStore } from 'pinia'
import { streamText } from '@xsai/stream-text'
import { useOpenAIProviderStore } from '../providers/openai'
import { useCharacterStore } from '../character'

export const useConsciousnessStore = defineStore('consciousness', () => {
  const providerStore = useOpenAIProviderStore()
  const characterStore = useCharacterStore()

  async function processInput(input: { text: string }) {
    if (!providerStore.client) {
      throw new Error('No LLM provider configured')
    }

    const messages = [
      { role: 'system' as const, content: characterStore.systemPrompt },
      ...characterStore.history,
      { role: 'user' as const, content: input.text },
    ]

    const stream = await streamText({
      model: providerStore.client(providerStore.model),
      messages,
    })

    let fullResponse = ''
    for await (const chunk of stream.textStream) {
      fullResponse += chunk
      // Emit chunk to UI progressively
      characterStore.appendChunk(chunk)
    }
    characterStore.finishMessage()
    return fullResponse
  }

  return { processInput }
})
```

---

## 15.6 Composant Vue avec Three VRM

### 15.6.1 VRM scene basique

```vue
<!-- src/components/VrmScene.vue -->
<script setup lang="ts">
import { TresCanvas } from '@tresjs/core'
import { ref, onMounted } from 'vue'
import { useVRM } from '@proj-airi/stage-ui-three/composables/vrm/core'
import { useLipSync } from '@proj-airi/stage-ui-three/composables/vrm/lip-sync'

const modelUrl = ref('/assets/vrm/hiyori.vrm')
const { vrm, loadVRM } = useVRM()
const { attachToAudio } = useLipSync()

onMounted(async () => {
  await loadVRM(modelUrl.value)
  attachToAudio(vrm.value)
})
</script>

<template>
  <TresCanvas clear-color="#f0f0f0" shadows>
    <TresPerspectiveCamera :position="[0, 1, 3]" :look-at="[0, 1, 0]" />
    <TresAmbientLight :intensity="0.8" />
    <TresDirectionalLight :position="[5, 10, 5]" :intensity="1.2" cast-shadow />
    <primitive v-if="vrm" :object="vrm.scene" />
  </TresCanvas>
</template>
```

### 15.6.2 Live2D avec OPFS cache

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Live2D } from '@proj-airi/stage-ui-live2d/components/scenes/Live2D.vue'
import { registerLive2DOPFS } from '@proj-airi/stage-ui-live2d/utils/live2d-opfs-registration'

const modelName = ref('hiyori')

onMounted(async () => {
  // Pré-charge le modèle dans OPFS depuis un ZIP
  const response = await fetch('/models/hiyori.zip')
  const blob = await response.blob()
  await registerLive2DOPFS(modelName.value, blob)
})
</script>

<template>
  <Live2D :model-name="modelName" />
</template>
```

---

## 15.7 Service bot Telegram complet

```typescript
// services/telegram-bot/src/index.ts
import { Bot, Context } from 'grammy'
import { Client } from '@proj-airi/server-sdk'

const airi = new Client({
  name: 'telegram',
  url: process.env.AIRI_URL ?? 'ws://localhost:6121/ws',
  token: process.env.AIRI_TOKEN,
  possibleEvents: ['input:text', 'output:gen-ai:chat:message'],
})

const bot = new Bot<Context>(process.env.TELEGRAM_TOKEN!)

// Map chatId → message Telegram qu'on est en train de streamer
const streamingMessages = new Map<number, number>()

bot.on('message:text', async (ctx) => {
  const chatId = ctx.chat.id
  const messageText = ctx.message.text

  airi.send({
    type: 'input:text',
    data: {
      text: messageText,
      overrides: { sessionId: `telegram-${chatId}` },
    },
  })

  // Préparer un message vide pour le streaming
  const placeholder = await ctx.reply('...')
  streamingMessages.set(chatId, placeholder.message_id)
})

airi.onEvent('output:gen-ai:chat:message', async (event) => {
  const sessionId = event.data.input?.data?.overrides?.sessionId
  if (!sessionId?.startsWith('telegram-')) return

  const chatId = Number(sessionId.replace('telegram-', ''))
  const messageId = streamingMessages.get(chatId)
  if (!messageId) return

  await bot.api.editMessageText(
    chatId,
    messageId,
    event.data.message.content,
  )
  streamingMessages.delete(chatId)
})

await airi.ready()
bot.start()
console.log('Telegram bot ready')
```

---

## 15.8 Worker VAD (Voice Activity Detection)

### 15.8.1 Démarrer le worker côté main thread

```typescript
// renderer/composables/useVad.ts
import { ref, onMounted, onUnmounted } from 'vue'
import VadWorker from '@proj-airi/stage-ui/workers/vad/worker?worker'

export function useVad(options: { sampleRate: number, threshold: number }) {
  const isListening = ref(false)
  const worker = new VadWorker()

  worker.postMessage({ type: 'configure', ...options })

  const speechBuffer: Float32Array[] = []

  worker.addEventListener('message', (event) => {
    const { type } = event.data
    if (type === 'speech-start') {
      isListening.value = true
      speechBuffer.length = 0
    }
    else if (type === 'speech-end') {
      isListening.value = false
      onSpeechEnd(event.data.audio)
    }
  })

  async function start() {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    const audioContext = new AudioContext({ sampleRate: options.sampleRate })
    const source = audioContext.createMediaStreamSource(stream)
    const processor = audioContext.createScriptProcessor(4096, 1, 1)
    source.connect(processor)
    processor.connect(audioContext.destination)
    processor.onaudioprocess = (e) => {
      const input = e.inputBuffer.getChannelData(0)
      worker.postMessage({ type: 'feed', buffer: input }, [input.buffer])
    }
  }

  function onSpeechEnd(audio: Float32Array) {
    // transcribe → input:text:voice
  }

  onUnmounted(() => worker.terminate())
  return { isListening, start }
}
```

---

## 15.9 Tests Vitest

### 15.9.1 Test du Client SDK

```typescript
// packages/server-sdk/src/client.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { Client } from './client'

class MockWebSocket {
  static OPEN = 1
  readyState = 1
  onopen?: () => void
  onmessage?: (event: any) => void
  onclose?: () => void
  onerror?: (event: any) => void
  sent: string[] = []

  constructor(public url: string) {
    setTimeout(() => this.onopen?.(), 0)
  }

  send(data: string) { this.sent.push(data) }
  close() { this.onclose?.() }
}

describe('Client', () => {
  let ws: MockWebSocket

  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('sends module:announce after connect', async () => {
    const client = new Client({
      name: 'test-module',
      url: 'ws://localhost:6121/ws',
      websocketConstructor: MockWebSocket as any,
    })

    await vi.runAllTimersAsync()
    expect(ws.sent).toContainEqual(
      expect.stringContaining('"type":"module:announce"'),
    )
  })

  it('reconnects with exponential backoff', async () => {
    // ...
  })
})
```

### 15.9.2 Test d'un store Pinia

```typescript
import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useOpenAIProviderStore } from './openai'

describe('useOpenAIProviderStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('is not configured by default', () => {
    const store = useOpenAIProviderStore()
    expect(store.isConfigured).toBe(false)
    expect(store.client).toBeNull()
  })

  it('becomes configured after setting apiKey', () => {
    const store = useOpenAIProviderStore()
    store.apiKey = 'sk-test'
    expect(store.isConfigured).toBe(true)
    expect(store.client).not.toBeNull()
  })
})
```

---

## 15.10 Ajout d'un nouveau fournisseur LLM

```typescript
// packages/stage-ui/src/stores/providers/mistral/index.ts
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { buildOpenAICompatibleProvider } from '../openai-compatible-builder'

export const useMistralProviderStore = defineStore('provider-mistral', () => {
  const apiKey = ref('')
  const baseURL = ref('https://api.mistral.ai/v1')
  const model = ref('mistral-large-latest')

  const { client, isConfigured } = buildOpenAICompatibleProvider({
    apiKey,
    baseURL,
    providerId: 'mistral',
  })

  return { apiKey, baseURL, model, client, isConfigured }
}, { persist: true })
```

Ensuite, enregistrer le store dans le ProviderManager (`stage-ui/src/stores/providers.ts`) :

```typescript
export const useProviderRegistry = defineStore('provider-registry', () => {
  return {
    providers: {
      openai: useOpenAIProviderStore(),
      openrouter: useOpenRouterProviderStore(),
      mistral: useMistralProviderStore(),  // ← ajout
      // ...
    },
  }
})
```

Enfin, ajouter la page de config dans `stage-pages/src/settings/providers/mistral.vue`.

---

## 15.11 Génération d'un QR code d'onboarding

```typescript
// apps/stage-tamagotchi/src/main/services/airi/channel-server.ts
import { encodeQrPayload } from '@proj-airi/stage-shared/server-channel-qr'
import os from 'node:os'

export function getServerChannelQrPayload(port: number, token?: string) {
  const interfaces = os.networkInterfaces()
  const urls: string[] = []

  for (const iface of Object.values(interfaces)) {
    if (!iface) continue
    for (const addr of iface) {
      if (addr.family === 'IPv4' && !addr.internal) {
        urls.push(`ws://${addr.address}:${port}/ws`)
      }
    }
  }
  urls.push(`ws://localhost:${port}/ws`)

  return encodeQrPayload({
    version: 1,
    urls,
    token,
    name: 'AIRI Local',
  })
}
```

Et la lecture côté mobile :

```typescript
// apps/stage-pocket/src/modules/server-channel-qr-probe.ts
import { parseQrPayload } from '@proj-airi/stage-shared/server-channel-qr'
import { Client } from '@proj-airi/server-sdk'
import { HostWebSocket } from './websocket-bridge'

export async function probeAndConnect(qrString: string): Promise<Client | null> {
  const payload = parseQrPayload(qrString)

  for (const url of payload.urls) {
    try {
      const client = new Client({
        name: 'proj-airi:stage-pocket',
        url,
        token: payload.token,
        websocketConstructor: HostWebSocket as any,
        autoConnect: false,
      })
      await client.connect({ timeout: 2500 })
      return client
    }
    catch {
      continue
    }
  }
  return null
}
```

---

## 15.12 Résumé : où placer chaque exemple

| Exemple | Répertoire |
|---------|-----------|
| Client SDK | n'importe où (plugin, service, renderer) |
| Plugin sdk definePlugin | `plugins/<nom>/src/index.ts` |
| Eventa contract | `apps/stage-tamagotchi/src/shared/eventa.ts` |
| Main handler | `apps/stage-tamagotchi/src/main/services/` |
| Renderer invoke | `apps/stage-tamagotchi/src/renderer/composables/` |
| Pinia store provider | `packages/stage-ui/src/stores/providers/<name>/` |
| Pinia store module | `packages/stage-ui/src/stores/modules/` |
| Composant VRM | `packages/stage-ui/src/components/scenes/` |
| Worker | `packages/stage-ui/src/workers/` |
| Test vitest | Collocated avec le code (`.test.ts`) |
| Service bot | `services/<nom>/src/index.ts` |
