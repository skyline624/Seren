# 06 — Packages serveur

Ce document détaille les packages qui forment la « spine dorsale » du bus de communication AIRI : `server-runtime`, `server-sdk`, `server-shared`, `plugin-protocol`, `plugin-sdk`.

## 6.1 `@proj-airi/plugin-protocol` — Contrats d'événements

### 6.1.1 Rôle

**C'est le contrat unique** entre tous les modules de l'écosystème AIRI. Ce package n'exporte que des **types TypeScript** et des **définitions eventa**, pas d'implémentation runtime. Tout ce qui transite sur le bus WebSocket est typé par ce package.

### 6.1.2 Fichier clé

`packages/plugin-protocol/src/types/events.ts` (~1400 lignes)

### 6.1.3 Types fondamentaux

#### `ModuleIdentity`

```typescript
interface ModuleIdentity {
  id: string              // ex: "telegram-01"
  kind: 'plugin'
  plugin: PluginIdentity
  labels?: Record<string, string>
}

interface PluginIdentity {
  id: string              // ex: "telegram-bot"
  version?: string        // ex: "0.8.1-beta.7"
  labels?: Record<string, string>
}
```

Chaque peer connecté au server-runtime a une identité. L'`id` est unique par instance, le `plugin.id` est partagé entre toutes les instances d'un même plugin.

#### `WebSocketBaseEvent<T, D>`

```typescript
interface WebSocketBaseEvent<T, D, S extends string = string> {
  type: T
  data: D
  /** @deprecated Prefer metadata.source. */
  source?: WebSocketEventSource | S
  metadata: {
    source: ModuleIdentity
    event: {
      id: string
      parentId?: string
    }
  }
  route?: RouteConfig
}
```

Tous les messages sur le bus héritent de cette structure. Le `metadata.event.id` est un identifiant unique par message (nanoid) ; `parentId` lie un message à son ancêtre causal (utile pour tracer une conversation).

#### `DeliveryConfig`

Pour routage avancé :

```typescript
type DeliveryMode = 'broadcast' | 'consumer' | 'consumer-group'
type DeliverySelectionStrategy = 'first' | 'round-robin' | 'priority' | 'sticky'

interface DeliveryConfig {
  mode?: DeliveryMode
  group?: string
  required?: boolean       // si true, erreur si pas de consumer dispo
  selection?: DeliverySelectionStrategy
  stickyKey?: string
}
```

- **broadcast** : envoyé à tous les peers abonnés (par défaut)
- **consumer** : un seul peer reçoit (singleton), sélectionné selon `selection`
- **consumer-group** : load-balancing dans un groupe nommé

#### `RouteTargetExpression` / `RouteConfig`

Permet de router un message vers des destinations précises :

```typescript
type RouteTargetExpression =
  | { type: 'and', all: RouteTargetExpression[] }
  | { type: 'or', any: RouteTargetExpression[] }
  | { type: 'glob', glob: string, inverted?: boolean }
  | { type: 'ids', ids: string[], inverted?: boolean }
  | { type: 'plugin', plugins: string[], inverted?: boolean }
  | { type: 'instance', instances: string[], inverted?: boolean }
  | { type: 'label', selectors: string[], inverted?: boolean }
  | { type: 'module', modules: string[], inverted?: boolean }
  | { type: 'source', sources: string[], inverted?: boolean }

interface RouteConfig {
  destinations?: Array<string | RouteTargetExpression>
  bypass?: boolean
  delivery?: DeliveryConfig
}
```

Exemple : router un message « uniquement vers les plugins avec label `env=prod` » :

```typescript
client.send({
  type: 'output:gen-ai:chat:message',
  data: { /* ... */ },
  route: {
    destinations: [{ type: 'label', selectors: ['env=prod'] }],
  },
})
```

### 6.1.4 Les types d'événements standardisés

Toutes les interfaces dérivent de `ProtocolEvents<C>` qui est un dictionnaire géant `{[eventName]: EventData}`. Extraits :

#### Événements de contrôle

| Event | Data | Direction |
|-------|------|-----------|
| `module:authenticate` | `{ token: string }` | client → server |
| `module:authenticated` | `{ authenticated: boolean }` | server → client |
| `module:announce` | `{ name, identity, possibleEvents, permissions?, configSchema?, dependencies? }` | client → server |
| `module:announced` | `{ name, index?, identity }` | server → all |
| `module:de-announced` | `{ name, index?, identity, reason? }` | server → all |
| `module:prepared` | `{ identity, missingDependencies? }` | client → server |
| `module:status` | `{ identity, phase, reason?, details? }` | bidir |
| `registry:modules:sync` | `{ modules: [...] }` | server → client |
| `registry:modules:health:healthy` | `{ name, index?, identity }` | server → all |
| `registry:modules:health:unhealthy` | `{ name, index?, identity, reason? }` | server → all |
| `transport:connection:heartbeat` | `{ kind: 'ping'|'pong', message, at }` | bidir |
| `error` | `{ message }` | bidir |

#### Événements de configuration

Cycle **plan → validate → commit → configured** :

- `module:configuration:validate:request` / `response` / `status`
- `module:configuration:plan:request` / `response` / `status`
- `module:configuration:commit` / `status`
- `module:configuration:configured`
- `module:configuration:needed`

#### Événements de contribution capabilities

- `module:contribute:capability:offer`
- `module:contribute:capability:configuration:needed`
- `module:contribute:capability:configuration:*`
- `module:contribute:capability:activated`

#### Événements de permissions

- `module:permissions:declare`
- `module:permissions:request`
- `module:permissions:granted`
- `module:permissions:denied`
- `module:permissions:current`

#### Événements d'entrée utilisateur

```typescript
interface WebSocketEventInputTextBase {
  text: string
  textRaw?: string
  overrides?: InputMessageOverrides
  contextUpdates?: InputContextUpdate[]
  contextPrompt?: string
  attachments?: Array<{ type: 'image', data: string, mimeType: string }>
}

// Avec source optionnelle pour distinguer web/desktop/discord
type WebSocketEventInputText = WebSocketEventInputTextBase
  & Partial<WithInputSource<'stage-web' | 'stage-tamagotchi' | 'discord'>>
```

| Event | Data | Rôle |
|-------|------|------|
| `input:text` | `WebSocketEventInputText` | Message texte utilisateur |
| `input:text:voice` | `{ transcription, textRaw?, overrides?, contextUpdates?, ...source }` | Voix transcrite en texte |
| `input:voice` | `{ audio: ArrayBuffer, overrides?, contextUpdates?, ...source }` | Audio brut à transcrire côté serveur |

#### Événements de sortie

```typescript
interface OutputSource {
  'gen-ai:chat': {
    message: UserMessage
    contexts: Record<string, ContextUpdate<Record<string, any>, unknown>[]>
    composedMessage: Array<Message>
    input?: InputEventEnvelope
  }
}
```

Event type : `output:gen-ai:chat:message`

### 6.1.5 Les enums

```typescript
enum MessageHeartbeatKind {
  Ping = 'ping',
  Pong = 'pong',
}

enum MessageHeartbeat {
  Ping = '🩵',
  Pong = '💛',
}

enum WebSocketEventSource {
  Server = 'proj-airi:server-runtime',
  StageWeb = 'proj-airi:stage-web',
  StageTamagotchi = 'proj-airi:stage-tamagotchi',
}
```

### 6.1.6 `ContextUpdate` — un mécanisme clef

Les modules peuvent envoyer des **fragments de contexte** qui enrichissent le prompt :

```typescript
interface ContextUpdate<Metadata = Record<string, unknown>, Content = undefined> {
  id: string
  contextId: string
  lane?: string                    // groupe logique (ex: "memory", "vision")
  ideas?: string[]
  hints?: string[]
  strategy: 'replace-self' | 'append-self'
  text: string
  content?: Content
  destinations?: string[] | ContextUpdateDestinationFilter
  metadata?: Metadata
}
```

Exemple : un plugin vision envoie un context update après analyse d'image, qui sera fusionné dans le prompt par le LLM orchestrator.

---

## 6.2 `@proj-airi/server-shared` — Validation + erreurs

### 6.2.1 Rôle

Code *runtime* partagé entre le server-runtime et le server-sdk. Contient :
- Fonction de validation d'enveloppe WebSocket (sans Zod pour ne pas polluer la dep graph)
- Codes d'erreur standardisés
- Re-export des types de `plugin-protocol`

### 6.2.2 `validateWebSocketEventEnvelope`

Fichier : `packages/server-shared/src/protocol/validate-event.ts`

```typescript
export function validateWebSocketEventEnvelope(value: unknown): EventValidationResult {
  if (!isPlainObject(value)) {
    return { ok: false, reason: 'event must be a plain object' }
  }
  if (!('type' in value)) {
    return { ok: false, reason: 'event is missing the "type" field' }
  }
  const type = value.type
  if (typeof type !== 'string' || type.length === 0) {
    return { ok: false, reason: 'event.type must be a non-empty string' }
  }
  if ('metadata' in value && value.metadata !== undefined && !isPlainObject(value.metadata)) {
    return { ok: false, reason: 'event.metadata, when present, must be a plain object' }
  }
  if ('data' in value && value.data !== undefined && value.data !== null) {
    const data = value.data
    if (!isPlainObject(data) && !Array.isArray(data)) {
      return { ok: false, reason: 'event.data, when present, must be an object, array, or null' }
    }
  }
  return { ok: true, event: value as unknown as ValidatedEventEnvelope }
}
```

**Important** : le commentaire dans le code explique que ce module est intentionnellement **sans Zod** (pour éviter d'injecter une dep lourde dans server-runtime et server-sdk). Phase 2 le remplacera par une union discriminée Zod.

### 6.2.3 Codes d'erreur

`packages/server-shared/src/errors.ts` expose `ServerErrorMessages` :

```typescript
export const ServerErrorMessages = {
  notAuthenticated: 'Not authenticated. Send module:authenticate first.',
  invalidToken: 'Invalid authentication token',
  rateLimited: 'Rate limit exceeded',
  invalidJson: 'Invalid JSON payload',
  // ...
} as const
```

Les clients peuvent comparer `event.data.message` avec ces constantes pour décider d'un retry ou d'un abandon définitif.

---

## 6.3 `@proj-airi/server-runtime` — Runtime WebSocket

### 6.3.1 Rôle

Serveur WebSocket central qui joue le rôle de **hub** pour tous les modules. Bâti sur **H3** + **crossws** + **superjson**.

### 6.3.2 Arborescence

```
packages/server-runtime/src/
├── index.ts              # setupApp() - usine principale (~1000+ lignes)
├── bin/
│   └── run.ts            # CLI pour lancer en standalone
├── server/
│   └── index.ts          # createServer() - wrapper H3 complet
├── types/
│   └── conn.ts           # Types peer/connection
├── middlewares/          # Routing + policies
├── config.ts             # optionOrEnv helper
└── bootkit/              # Lifecycle hooks
```

### 6.3.3 `setupApp()` — ce qui est construit

La fonction `setupApp()` (dans `src/index.ts` ligne ~376) retourne une instance H3 avec toutes les closures nécessaires pour gérer :

1. **Registry de peers** : `Map<peerId, AuthenticatedPeer>`
2. **Peers par nom de module** : `Map<moduleName, Map<index, AuthenticatedPeer>>`
3. **Registry de consumers** : `Map<eventType, Map<group, Map<peerId, ConsumerRegistration>>>`
4. **Health monitor** (interval)
5. **Rate limiter** par peer (sliding window)
6. **Origin/CORS check**
7. **Routing middleware pipeline**
8. **Logger** via `@guiiai/logg`

### 6.3.4 Authenticated peer

```typescript
interface AuthenticatedPeer {
  peer: Peer
  authenticated: boolean
  name?: string
  index?: number              // multi-instance support
  identity?: ModuleIdentity
  lastHeartbeatAt: number
  healthy: boolean
  missedHeartbeats: number
}
```

### 6.3.5 Flow de connexion (WebSocket handler)

Défini via `defineWebSocketHandler` de h3 :

```typescript
const websocketHandler = defineWebSocketHandler({
  async upgrade(request) {
    // Origin / CORS check
    if (!checkOrigin(request.headers.origin)) {
      return new Response('Forbidden', { status: 403 })
    }
  },

  async open(peer) {
    const peerId = peer.id
    peers.set(peerId, {
      peer,
      authenticated: !authTokenRequired,
      lastHeartbeatAt: Date.now(),
      healthy: true,
      missedHeartbeats: 0,
    })
    if (!authTokenRequired) {
      sendRegistrySync(peer)
    }
  },

  async message(peer, message) {
    const state = peers.get(peer.id)
    if (!state) return

    // Rate limiting
    if (!rateLimiter.check(peer.id)) {
      return  // silently drop
    }

    // Heartbeat control frame
    const text = message.text()
    const heartbeatKind = detectHeartbeatControlFrame(text)
    if (heartbeatKind) {
      state.lastHeartbeatAt = Date.now()
      if (heartbeatKind === 'ping') send(peer, RESPONSES.heartbeat('pong', MessageHeartbeat.Pong, serverInstanceId))
      return
    }

    // Parse envelope
    let parsed: unknown
    try {
      parsed = superjson.parse(text)
    } catch {
      try {
        parsed = JSON.parse(text)
      } catch {
        send(peer, createInvalidJsonServerErrorMessage(serverInstanceId))
        return
      }
    }

    // Validate envelope
    const validation = validateWebSocketEventEnvelope(parsed)
    if (!validation.ok) {
      send(peer, RESPONSES.error(validation.reason, serverInstanceId))
      return
    }

    const event = validation.event as WebSocketEvent

    // Dispatch
    switch (event.type) {
      case 'transport:connection:heartbeat':
        state.lastHeartbeatAt = Date.now()
        if (event.data.kind === 'ping') {
          send(peer, RESPONSES.heartbeat('pong', MessageHeartbeat.Pong, serverInstanceId, event.metadata.event.id))
        }
        break

      case 'module:authenticate':
        const token = event.data.token
        if (!timingSafeCompare(token, expectedToken)) {
          send(peer, RESPONSES.notAuthenticated(serverInstanceId, event.metadata.event.id))
          return
        }
        state.authenticated = true
        send(peer, RESPONSES.authenticated(serverInstanceId, event.metadata.event.id))
        sendRegistrySync(peer)
        break

      case 'module:announce':
        if (!state.authenticated) { /* error */ return }
        state.name = event.data.name
        state.identity = event.data.identity
        peersByModule.get(state.name)?.set(state.index ?? 0, state)
        broadcastAnnounced(state)
        break

      case 'module:consumer:register':
        registerConsumer(event.data, peer.id)
        break

      case 'module:consumer:unregister':
        unregisterConsumer(event.data, peer.id)
        break

      default:
        // Application event — apply routing
        if (!state.authenticated) { /* error */ return }
        const routeContext = buildRouteContext(event, state)
        const decision = applyMiddleware(routeContext)
        if (decision === 'drop') return

        const delivery = resolveDeliveryConfig(event)
        const targets = selectTargets(event, delivery, peers)
        for (const target of targets) {
          send(target.peer, stringify(event))
        }
    }
  },

  async close(peer, _details) {
    peers.delete(peer.id)
    broadcastDeAnnounced(peer)
  },
})
```

### 6.3.6 Protection timing-safe contre les attaques (CWE-208)

```typescript
function timingSafeCompare(a: string, b: string): boolean {
  const bufA = Buffer.from(a)
  const bufB = Buffer.from(b)
  if (bufA.length !== bufB.length) {
    timingSafeEqual(bufA, bufA)  // garder un temps constant
    timingSafeEqual(bufB, bufB)
    return false
  }
  return timingSafeEqual(bufA, bufB)
}
```

### 6.3.7 Health monitor

Toutes les `healthCheckIntervalMs` (défaut 10s) :

1. Pour chaque peer, `missedHeartbeats += 1` si `now - lastHeartbeatAt > threshold`
2. Si `missedHeartbeats >= 5` → marquer `healthy = false`, broadcast `registry:modules:health:unhealthy`
3. Si `missedHeartbeats >= 10` → fermer la connexion + broadcast `module:de-announced`

### 6.3.8 Rate limiting

Window sliding par défaut 100 events / 10s, mesuré par peer. Les heartbeats sont exemptés. Si dépassé → drop silencieux.

### 6.3.9 Démarrage standalone

```bash
pnpm -F @proj-airi/server-runtime dev
# équivalent à :
cd packages/server-runtime && tsx src/bin/run.ts --port 6121 --token <optional>
```

Variables d'environnement supportées :
- `AIRI_SERVER_PORT` (défaut 6121)
- `AIRI_SERVER_AUTH_TOKEN` (optionnel)
- `AIRI_SERVER_CORS_ORIGINS` (liste séparée par virgule)
- `AIRI_SERVER_RATE_LIMIT_MAX`, `AIRI_SERVER_RATE_LIMIT_WINDOW_MS`
- `AIRI_SERVER_READ_TIMEOUT_MS`
- Plus `LOG_LEVEL`, `LOG_FORMAT`

### 6.3.10 Middlewares de routing

Extrait de `middlewares/index.ts` :

```typescript
export interface RouteContext {
  event: WebSocketEvent
  source: AuthenticatedPeer
  peers: Map<string, AuthenticatedPeer>
  peersByModule: Map<string, Map<number, AuthenticatedPeer>>
}

export type RouteDecision = 'drop' | 'continue' | { targets: AuthenticatedPeer[] }

export type RouteMiddleware = (ctx: RouteContext, next: () => RouteDecision) => RouteDecision

// Middleware de policy : filtre selon plugin IDs / labels
export function createPolicyMiddleware(policy: RoutingPolicy): RouteMiddleware { /* ... */ }
```

Le pipeline est ordonné et chaque middleware peut court-circuiter (drop) ou transformer la liste de targets.

---

## 6.4 `@proj-airi/server-sdk` — Client SDK

### 6.4.1 Rôle

SDK client pour parler au server-runtime. Fournit la classe `Client` qui encapsule :
- Connexion WebSocket
- Handshake auth + announce
- Heartbeat automatique
- Reconnexion avec back-off exponentiel
- Dispatch typé des événements

### 6.4.2 Exports

```json
{
  "exports": {
    ".": "./src/index.ts",
    "./utils/node": "./src/utils/node/index.ts"
  }
}
```

- `./` exporte `Client` et les types principaux
- `./utils/node` exporte des utilitaires Node.js (ex: wrappers autour de `ws`)

### 6.4.3 La classe `Client<C>`

Fichier : `packages/server-sdk/src/client.ts`

```typescript
export class Client<C = undefined> {
  private websocket?: WebSocketLike
  private shouldClose = false
  private connectTask?: Promise<void>
  private heartbeatTimer?: ReturnType<typeof setInterval>
  private lastPingAt = 0
  private lastReadAt = 0
  private reconnectAttempts = 0
  private status: ClientStatus = 'idle'
  private readonly identity: MetadataEventSource
  private readonly heartbeat: Required<ClientHeartbeatOptions>
  private readonly websocketConstructor: WebSocketLikeConstructor

  private readonly eventListeners = new Map<
    keyof WebSocketEvents<C>,
    Set<(data: WebSocketBaseEvent<any, any>) => void | Promise<void>>
  >()

  constructor(options: ClientOptions<C>) { /* ... */ }

  get connectionStatus(): ClientStatus { return this.status }
  get isReady(): boolean { return this.status === 'ready' }
  get isSocketOpen(): boolean { /* ... */ }
  get lastError(): Error | undefined { return this.failureReason }

  async connect(options?: ConnectOptions): Promise<void> { /* ... */ }
  ready(options?: ConnectOptions) { return this.connect(options) }

  onEvent<E extends keyof WebSocketEvents<C>>(
    event: E,
    callback: (data: WebSocketBaseEvent<E, WebSocketEvents<C>[E]>) => void | Promise<void>,
  ): () => void { /* ... */ }

  offEvent<E>(event: E, callback?: Fn): void { /* ... */ }

  send(data: WebSocketEventOptionalSource<C>): boolean { /* ... */ }
  sendOrThrow(data: WebSocketEventOptionalSource<C>): void { /* ... */ }
  sendRaw(data: string | ArrayBuffer): boolean { /* ... */ }

  close(): void { /* ... */ }

  onConnectionStateChange(cb: (ctx: ClientStateChangeContext) => void): () => void { /* ... */ }
}
```

### 6.4.4 Options de construction

```typescript
interface ClientOptions<C = undefined> {
  name: string
  url?: string                  // défaut: ws://localhost:6121/ws
  token?: string                // optionnel (triggers auth handshake)
  websocketConstructor?: WebSocketLikeConstructor  // pour environnements custom (mobile, test)
  identity?: MetadataEventSource
  dependencies?: ModuleDependency[]
  configSchema?: ModuleConfigSchema
  possibleEvents?: (keyof WebSocketEvents<C>)[]
  heartbeat?: {
    pingInterval?: number       // défaut: readTimeout / 2
    readTimeout?: number        // défaut: 30_000 ms
    message?: MessageHeartbeat
  }
  autoConnect?: boolean         // défaut: true
  autoReconnect?: boolean       // défaut: true
  maxReconnectAttempts?: number // défaut: -1 (illimité)
  connectTimeoutMs?: number     // défaut: 15_000

  onError?: (error: unknown) => void
  onClose?: () => void
  onReady?: () => void
  onStateChange?: (ctx: ClientStateChangeContext) => void
  onAnyMessage?: (data: WebSocketEvent) => void
  onAnySend?: (data: WebSocketEvent) => void
}
```

### 6.4.5 États (`ClientStatus`)

```
idle → connecting → authenticating (si token) → announcing → ready
                                      ↓
                                   closing → idle
```

### 6.4.6 Reconnexion avec back-off exponentiel

Fichier `client.ts:312-355` :

```typescript
private async scheduleReconnect() {
  if (!this.opts.autoReconnect) return
  if (this.opts.maxReconnectAttempts !== -1 && this.reconnectAttempts >= this.opts.maxReconnectAttempts) {
    return
  }
  this.reconnectAttempts++
  const delay = Math.min(2 ** this.reconnectAttempts * 1000, 30_000)
  await sleep(delay)
  if (!this.shouldClose) {
    await this.runConnectLoop()
  }
}
```

- 1ère retry : 2s
- 2ème : 4s
- 3ème : 8s
- ...
- Plafonné à 30s

Les erreurs terminales (token invalide) NE déclenchent PAS de retry : on `close()` immédiatement.

### 6.4.7 Heartbeat

Fichier `client.ts:803-876` :

```typescript
private startHeartbeat() {
  this.lastPingAt = Date.now()
  this.lastReadAt = Date.now()
  this.heartbeatTimer = setInterval(() => {
    const now = Date.now()
    if (now - this.lastReadAt > this.heartbeat.readTimeout) {
      // Pas de réponse dans les temps → disconnect
      this.websocket?.close()
      return
    }
    if (now - this.lastPingAt >= this.heartbeat.pingInterval) {
      this.sendHeartbeatPing()
      this.lastPingAt = now
    }
  }, this.heartbeat.pingInterval / 2)
}
```

Envoie à la fois un message `transport:connection:heartbeat` et un ping WebSocket natif.

### 6.4.8 Exemple d'utilisation

```typescript
import { Client } from '@proj-airi/server-sdk'

const client = new Client({
  name: 'my-plugin',
  url: 'ws://localhost:6121/ws',
  token: process.env.AIRI_TOKEN,
  possibleEvents: ['input:text', 'output:gen-ai:chat:message'],
  dependencies: [{ role: 'llm:orchestrator', optional: false }],
  configSchema: {
    id: 'my-plugin.config',
    version: 1,
    schema: {
      type: 'object',
      properties: {
        model: { type: 'string' },
      },
      required: ['model'],
    },
  },
  onReady: () => console.log('Connected and announced'),
  onError: err => console.error('Error:', err),
})

// S'abonner à un event
const unsubscribe = client.onEvent('output:gen-ai:chat:message', (event) => {
  console.log('Got response:', event.data)
})

// Envoyer un event
client.send({
  type: 'input:text',
  data: {
    text: 'Bonjour AIRI !',
    'stage-tamagotchi': true,
  },
})

// Plus tard : cleanup
unsubscribe()
client.close()
```

### 6.4.9 Instance ID unique

Le constructeur génère un `instanceId` unique par `new Client(...)` :

```typescript
function createInstanceId(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`
}
```

Cela permet au serveur de distinguer plusieurs instances d'un même plugin (ex: deux workers Telegram).

---

## 6.5 `@proj-airi/plugin-sdk` — SDK de plugin

### 6.5.1 Rôle

SDK de plus haut niveau que `server-sdk`, destiné à écrire des plugins AIRI complets avec machine à états (xstate), support multi-runtime (Node + Web), et gestion des capabilities.

### 6.5.2 Dépendances clés

- `@moeru/eventa`
- `@proj-airi/plugin-protocol`
- `xstate` (machine à états pour le cycle de vie du plugin)
- `valibot`
- `nanoid`

### 6.5.3 Entrée

`./src/index.ts` exporte :
- `createPluginHost()` — factory pour le runtime
- `definePlugin()` — helper pour définir un plugin avec type-safety
- Types : `PluginHost`, `Plugin`, `PluginCapability`, etc.

### 6.5.4 Exemple schématique

```typescript
import { definePlugin } from '@proj-airi/plugin-sdk'

export default definePlugin({
  id: 'my-plugin',
  version: '1.0.0',
  dependencies: [{ role: 'llm:orchestrator' }],
  configSchema: { /* valibot schema */ },

  async setup({ client, config, context }) {
    client.onEvent('input:text', async (event) => {
      // Process
      client.send({ type: 'output:gen-ai:chat:message', data: { /* ... */ } })
    })
  },

  async teardown() {
    // Cleanup
  },
})
```

---

## 6.6 `@proj-airi/server-sdk-shared` et `@proj-airi/server-schema`

Ces packages complètent l'infrastructure :

- **server-sdk-shared** : code partagé entre `server-sdk` et ses variantes (utilitaires, constantes)
- **server-schema** : schémas JSON décrivant les événements (utilisé pour la documentation, et potentiellement pour la validation Phase 2). Le cache de build est désactivé dans `turbo.json` car la sortie dépend du temps.
