# 10 — Protocole WebSocket & Eventa

Ce document formalise tous les protocoles de communication utilisés dans AIRI.

## 10.1 Protocole WebSocket AIRI

### 10.1.1 Transport

- **URL** : `ws://<host>:<port>/ws` (par défaut `ws://localhost:6121/ws`)
- **Sérialisation** : [superjson](https://github.com/flightcontrolhq/superjson) (JSON avec type metadata pour `Date`, `Map`, `Set`, `BigInt`, etc.)
- **Fallback** : si `superjson.parse()` échoue, `JSON.parse()` est tenté (pour les clients externes ne connaissant pas superjson)
- **Type MIME** : `text` (chaque frame WebSocket est un string JSON/superjson)

### 10.1.2 Schéma d'enveloppe

```typescript
interface WebSocketEventEnvelope {
  type: string          // ex: 'input:text', 'module:authenticate'
  data?: Record<string, unknown> | unknown[] | null
  metadata?: {
    source: ModuleIdentity
    event: {
      id: string        // nanoid()
      parentId?: string // id de l'event cause
    }
  }
  route?: RouteConfig   // routage explicite (optionnel)
}
```

Validation runtime : `validateWebSocketEventEnvelope()` — voir [06-packages-serveur.md § 6.2.2](06-packages-serveur.md).

### 10.1.3 Handshake de connexion

#### Cas 1 : Serveur SANS authentification

```
Client                              Server
  │                                    │
  │ ─── WebSocket upgrade (GET /ws) ─► │
  │ ◄──── 101 Switching Protocols ──── │
  │                                    │
  │                                    │
  │ ◄── registry:modules:sync ───── │  (sync initial des modules connus)
  │                                    │
  │ ── module:announce ──────────────► │  (envoi identity + possibleEvents)
  │                                    │
  │ ◄── module:announced ──────────── │  (broadcast à tous)
  │                                    │
  │                                    │
  │      Prêt à échanger des events    │
  │ ◄──────────────────────────────► │
```

#### Cas 2 : Serveur AVEC authentification token

```
Client                              Server
  │                                    │
  │ ─── WebSocket upgrade (GET /ws) ─► │
  │ ◄──── 101 Switching Protocols ──── │
  │                                    │
  │ ── module:authenticate {token} ──► │
  │                                    │  timingSafeCompare(token, expected)
  │ ◄── module:authenticated {ok} ─── │
  │                                    │
  │ ◄── registry:modules:sync ──────── │
  │                                    │
  │ ── module:announce ──────────────► │
  │ ◄── module:announced ─────────── │
  │                                    │
  │      Prêt à échanger des events    │
```

Si le token est invalide :
```
  │ ── module:authenticate {bad} ────► │
  │ ◄── error {'Not authenticated'} ─ │
  │ ◄── WebSocket close ────────────── │
```

### 10.1.4 Heartbeat

Deux mécanismes coexistent :

1. **Control frame WebSocket** (ping/pong au niveau TCP, via `websocket.ping()` / `pong()`)
2. **Message applicatif** `transport:connection:heartbeat` avec body `{ kind: 'ping'|'pong', message: '🩵'|'💛', at: timestamp }`

Paramètres par défaut (côté SDK) :
- `readTimeout: 30_000` ms (si aucun message reçu dans ce délai → déconnexion)
- `pingInterval: 15_000` ms (= readTimeout / 2) (envoi d'un ping périodique)

Côté serveur :
- `healthCheckIntervalMs: 10_000` ms (par défaut)
- Si aucun heartbeat dans `readTimeout`, `missedHeartbeats += 1` à chaque check
- `missedHeartbeats >= 5` → `registry:modules:health:unhealthy` broadcast
- `missedHeartbeats >= 10` → close peer + `module:de-announced`

### 10.1.5 Lifecycle d'un module

```
                    ┌─────────┐
                    │  idle   │
                    └────┬────┘
                         │ connect()
                         ▼
                    ┌─────────┐
                    │connecting│
                    └────┬────┘
                         │ (socket open)
                         ▼
             ┌───────────────────────┐
             │      auth required?   │
             └─────┬─────────────┬───┘
                No │          Yes│
                   │             ▼
                   │       ┌──────────────┐
                   │       │authenticating│
                   │       └──────┬───────┘
                   │              │ authenticated
                   │              │ (or fail → fail)
                   │              ▼
                   └────► ┌──────────┐
                          │announcing│
                          └──────┬───┘
                                 │ announced
                                 ▼
                          ┌──────────┐
                          │  ready   │◄────────┐
                          └────┬─────┘         │
                               │               │
                     close() / │               │ reconnect
                   socket err  │               │
                               ▼               │
                          ┌──────────┐         │
                          │ closing  ├─────────┘
                          └────┬─────┘
                               │
                               ▼
                          ┌──────────┐
                          │   idle   │
                          └──────────┘
```

### 10.1.6 Rate limiting

- **Algorithme** : sliding window par peer
- **Défaut** : 100 events / 10s
- **Exemptions** : heartbeats
- **Comportement** : silent drop (pas de notification au client)

### 10.1.7 Origin / CORS

À l'upgrade WebSocket, le serveur vérifie l'origine :
- **Liste par défaut autorisée** : `localhost` (tous ports), `127.0.0.1`, `::1`, requêtes sans `Origin` (tests, hors navigateur)
- **Configurable** via `AIRI_SERVER_CORS_ORIGINS` (liste séparée par virgule)
- **Si refus** : le serveur ferme immédiatement avec 403

### 10.1.8 Codes d'erreur

Les erreurs sont remontées comme des événements `type: 'error'` avec un message lisible :

| Message | Cause | Récupérable |
|---------|-------|------------|
| `Not authenticated. Send module:authenticate first.` | Tentative d'opération sans auth | ✅ (retry après auth) |
| `Invalid authentication token` | Token incorrect | ❌ (terminal, pas de retry) |
| `Rate limit exceeded` | Dépassement rate limit | ✅ (wait + retry) |
| `Invalid JSON payload` | Parse error | ⚠️ (bug côté client) |
| `event must be a plain object` | Envelope malformée | ⚠️ |
| `event.type must be a non-empty string` | Type manquant | ⚠️ |

Pour chaque erreur, `metadata.event.parentId` pointe vers l'ID du message qui a causé l'erreur, pour permettre le traçage.

### 10.1.9 Routage des événements

Quand un module envoie un event applicatif (pas un event de contrôle), le serveur applique :

1. **Collection des destinations** : depuis `event.route.destinations` ou `event.data.destinations`
2. **Middleware pipeline** : chaque middleware reçoit `{event, source, peers}` et retourne `'drop' | 'continue' | { targets }`
3. **Résolution du delivery mode** : via `getProtocolEventMetadata(event.type)?.delivery` + `event.route?.delivery`
4. **Si `mode === 'consumer' || 'consumer-group'`** : sélection selon `selection` strategy
5. **Sinon (`broadcast`)** : envoi à tous les peers authenticated qui ont déclaré `possibleEvents` contenant ce type

**Exemple de routage avancé** :

```typescript
client.send({
  type: 'output:gen-ai:chat:message',
  data: { /* ... */ },
  route: {
    destinations: [
      // Envoyer uniquement aux modules stage-* avec label env=prod
      {
        type: 'and',
        all: [
          { type: 'glob', glob: 'proj-airi:stage-*' },
          { type: 'label', selectors: ['env=prod'] },
        ],
      },
    ],
    delivery: {
      mode: 'broadcast',
    },
  },
})
```

### 10.1.10 Consumer register / unregister

Un module peut s'enregistrer pour consommer un type d'event particulier :

```typescript
client.send({
  type: 'module:consumer:register',
  data: {
    event: 'input:text',
    group: 'llm-orchestrator',  // groupe de load balancing
    priority: 10,               // priorité (+ haute = sélectionnée en premier)
    mode: 'consumer-group',
    selection: 'round-robin',
  },
})
```

Pour se désinscrire :

```typescript
client.send({
  type: 'module:consumer:unregister',
  data: { event: 'input:text', group: 'llm-orchestrator' },
})
```

---

## 10.2 Protocole Eventa IPC (Electron)

Dans `stage-tamagotchi`, en plus du protocole WebSocket, il y a un **second protocole IPC** basé sur `@moeru/eventa` entre le main process et le renderer.

### 10.2.1 Principe

`@moeru/eventa` fournit deux primitives :

- `defineEventa<P>(id)` — pub/sub
- `defineInvokeEventa<R, P>(id)` — RPC request/response

Les contrats sont **type-safe** : définis dans un fichier partagé (`src/shared/eventa.ts`), importés par le main et par le renderer.

### 10.2.2 Exemple : get window bounds

**Contrat** (`apps/stage-tamagotchi/src/shared/eventa.ts`) :

```typescript
import { defineInvokeEventa } from '@moeru/eventa'

export const electronGetWindowBounds = defineInvokeEventa<
  { x: number; y: number; width: number; height: number },  // Response
  { windowName: string }                                      // Payload
>('electron.window.getBounds')
```

**Main process** (handler) :

```typescript
import { defineInvokeHandler } from '@moeru/eventa/electron-main'
import { electronGetWindowBounds } from '../../shared/eventa'

defineInvokeHandler(electronGetWindowBounds, async ({ payload }) => {
  const window = getWindowByName(payload.windowName)
  return window.getBounds()
})
```

**Renderer** (caller) :

```typescript
import { invoke } from '@moeru/eventa/electron-renderer'
import { electronGetWindowBounds } from '../../../shared/eventa'

const bounds = await invoke(electronGetWindowBounds, { windowName: 'main' })
console.log(bounds)  // { x, y, width, height }
```

### 10.2.3 Exemple : lifecycle emit

**Contrat** :

```typescript
import { defineEventa } from '@moeru/eventa'

export const electronWindowLifecycleChanged = defineEventa<{
  windowName: string
  state: 'show' | 'hide' | 'minimize' | 'restore' | 'focus' | 'blur'
}>('electron.window.lifecycle.changed')
```

**Main** (emit) :

```typescript
import { emit } from '@moeru/eventa/electron-main'

mainWindow.on('focus', () => {
  emit(electronWindowLifecycleChanged, { windowName: 'main', state: 'focus' })
})
```

**Renderer** (listener) :

```typescript
import { on } from '@moeru/eventa/electron-renderer'

const unsubscribe = on(electronWindowLifecycleChanged, (event) => {
  console.log(`Window ${event.windowName}: ${event.state}`)
})
```

### 10.2.4 Liste exhaustive des contrats eventa

Extrait de `apps/stage-tamagotchi/src/shared/eventa.ts` (295 lignes, ~50 contrats) :

#### Windows & UI
```typescript
electronOpenSettings              // invoke, ouvre la settings window
electronOpenChat                  // invoke
electronOpenDevtoolsWindow        // invoke
electronSettingsNavigate          // emit, navigation interne settings
electronStartDraggingWindow       // emit, démarre un drag de fenêtre frameless
electronWindowLifecycleChanged    // emit
electronGetWindowLifecycleState   // invoke, retourne { visible, minimized, focused }
```

#### Server channel
```typescript
electronGetServerChannelConfig    // invoke
electronApplyServerChannelConfig  // invoke
electronGetServerChannelQrPayload // invoke, retourne le payload QR pour onboarding
```

#### Auto-updater
```typescript
electronGetUpdaterPreferences     // invoke
electronSetUpdaterPreferences     // invoke
// channel: 'stable' | 'alpha' | 'beta' | 'nightly' | 'canary'
```

#### Plugins
```typescript
electronPluginList                // invoke, liste tous les plugins + état
electronPluginLoad                // invoke, charge un plugin par path
electronPluginUnload              // invoke
electronPluginInspect             // invoke, retourne la capability snapshot
electronPluginUpdateCapability    // invoke
```

#### MCP (Model Context Protocol)
```typescript
electronMcpApplyAndRestart        // invoke, recharge la config MCP
electronMcpGetRuntimeStatus       // invoke
electronMcpListTools              // invoke
electronMcpCallTool               // invoke, appelle un tool MCP
```

#### Widgets
```typescript
widgetsAdd                        // invoke
widgetsRemove                     // invoke
widgetsFetch                      // invoke
widgetsUpdate                     // invoke
widgetsRenderEvent                // emit (internal)
widgetsRemoveEvent                // emit (internal)
```

#### Auth (OIDC flow)
```typescript
electronAuthStartLogin            // invoke, ouvre le browser sur l'URL d'auth
electronAuthCallback              // emit, reçu quand le callback OIDC arrive
electronAuthLogout                // invoke
// Token payload: { accessToken, refreshToken, idToken, expiresIn }
```

#### Tracing
```typescript
stageThreeRuntimeTraceForwardedEvent  // emit (metrics VRM → main)
```

### 10.2.5 Règle de stockage

> **Convention projet (AGENTS.md)** : toujours définir les contrats eventa dans un dossier partagé (`src/shared/`), jamais les dupliquer entre main et renderer. Centraliser permet de garantir que les deux côtés voient le même type.

### 10.2.6 Gestion de maxListeners

Par défaut, Node.js émet un warning si un EventEmitter a plus de 10 listeners. Avec 50+ handlers, stage-tamagotchi fait :

```typescript
import { ipcMain } from 'electron'
ipcMain.setMaxListeners(100)  // src/main/index.ts ligne 46
```

> **TODO inscrit dans le code** : `// TODO: once we refactored eventa to support window-namespaced contexts, we can remove the setMaxListeners call below...`

---

## 10.3 Protocole Web Worker

### 10.3.1 VAD worker

Dans `stage-ui/workers/vad` :

**Main thread → worker** :
```typescript
worker.postMessage({ type: 'configure', sampleRate: 16000, threshold: 0.5 })
worker.postMessage({ type: 'feed', buffer: float32Array }, [float32Array.buffer])
worker.postMessage({ type: 'destroy' })
```

**Worker → main thread** :
```typescript
// speech-start
{ type: 'speech-start', at: number }

// speech-end avec audio
{ type: 'speech-end', at: number, audio: Float32Array }
```

### 10.3.2 Kokoro TTS worker

Dans `stage-ui/workers/kokoro` :

```typescript
// Main → worker
worker.postMessage({ type: 'synthesize', text, voice: 'af_bella' })

// Worker → main (stream)
{ type: 'chunk', audio: Float32Array, visemes: VisemeFrame[] }
{ type: 'done' }
```

---

## 10.4 Protocole Capacitor bridge (iOS / Android)

### 10.4.1 iOS (WKScriptMessage)

Côté JS :
```typescript
window.webkit.messageHandlers.airiHostBridge.postMessage({
  action: 'connect' | 'send' | 'close',
  instanceId: string,
  url?: string,
  data?: string,
})
```

Côté Swift (réception) :
```swift
func userContentController(
  _ userContentController: WKUserContentController,
  didReceive message: WKScriptMessage
) {
  let body = message.body as? [String: Any]
  let action = body?["action"] as? String
  switch action {
    case "connect":  /* handle */
    case "send":     /* handle */
    case "close":    /* handle */
    default: break
  }
}
```

Côté Swift (émission) :
```swift
webView.evaluateJavaScript("""
  window.dispatchNativeMessage('\(instanceId)', 'message', \(jsonPayload))
""")
```

### 10.4.2 Android (JavaScriptInterface)

Côté JS :
```typescript
window.AiriHostBridge.connect(instanceId, url)
window.AiriHostBridge.send(instanceId, data)
window.AiriHostBridge.close(instanceId)
```

Côté Kotlin :
```kotlin
class WebSocketBridgeJavascriptInterface(private val bridge: HostWebSocketBridge) {
  @JavascriptInterface
  fun connect(instanceId: String, url: String) {
    bridge.connect(instanceId, url)
  }
  // ...
}

webView.addJavascriptInterface(
  WebSocketBridgeJavascriptInterface(bridge),
  "AiriHostBridge"
)
```

Côté Kotlin (émission vers JS) :
```kotlin
webView.post {
  webView.evaluateJavascript(
    "window.dispatchNativeMessage('$instanceId', '$eventType', $jsonPayload)",
    null
  )
}
```

---

## 10.5 Synthèse des couches de communication

```
┌─────────────────────────────────────────────────────┐
│       UI Vue (composants, pages, stores)            │
└──────────────┬──────────────────────┬───────────────┘
               │                      │
               │ (renderer only)      │
               ▼                      ▼
   ┌───────────────────────┐   ┌──────────────────┐
   │  eventa IPC (local)   │   │  server-sdk      │
   │  @moeru/eventa        │   │  Client          │
   └──────────┬────────────┘   └────────┬─────────┘
              │                          │
              │                          │ superjson
              ▼                          ▼
   ┌──────────────────────┐   ┌─────────────────────┐
   │  Electron IPC Main   │   │  WebSocket          │
   │  ipcMain ↔ preload   │   │  (crossws / native) │
   └──────────┬───────────┘   └─────────┬───────────┘
              │                          │
              ▼                          ▼
   ┌──────────────────────┐   ┌─────────────────────┐
   │  Main process logic  │   │  server-runtime     │
   │  (injeca, services)  │   │  dispatcher         │
   └──────────────────────┘   └─────────────────────┘
```

Chaque couche est **type-safe** grâce aux contrats partagés (`plugin-protocol/events.ts`, `shared/eventa.ts`).
