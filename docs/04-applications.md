# 04 — Applications (apps/)

Ce document détaille les six applications du monorepo. Chacune est un produit *déployable*.

## 4.1 `apps/stage-tamagotchi` — Desktop Electron

### 4.1.1 Rôle

Application desktop AIRI « officielle ». Embarque le server-runtime et offre l'expérience utilisateur complète : stage (avatar), chat, caption, settings, widgets, onboarding, tray, auto-update.

### 4.1.2 Arborescence

```
stage-tamagotchi/
├── build/                    # Resources pour electron-builder (icônes, entitlements)
├── resources/                # Assets embarqués (icon.png, fonts, images)
├── scripts/                  # Scripts CLI (update manifest generator, etc.)
├── src/
│   ├── main/                 # Process principal Electron
│   │   ├── app/              # Debugger, file-logger, lifecycle hooks
│   │   ├── configs/          # Config globale (valibot)
│   │   ├── libs/             # bootkit, electron/location, i18n
│   │   ├── services/
│   │   │   ├── airi/         # channel-server, mcp-servers, plugins, auth
│   │   │   └── electron/     # window.ts, auto-updater.ts, app.ts,
│   │   │                     # powerMonitor.ts, screen.ts, system-preferences.ts
│   │   ├── windows/          # Factories BrowserWindow
│   │   │   ├── main/         # Fenêtre stage principale
│   │   │   ├── settings/     # Fenêtre paramètres
│   │   │   ├── chat/         # Fenêtre chat
│   │   │   ├── caption/      # Overlay sous-titres
│   │   │   ├── widgets/      # Container widgets
│   │   │   ├── about/        # À propos
│   │   │   ├── onboarding/   # Premier lancement / onboarding serveur
│   │   │   ├── notice/       # Notifications overlay
│   │   │   ├── beat-sync/    # Fenêtre de fond pour audio
│   │   │   └── devtools/     # Markdown stress tests
│   │   ├── tray/             # Tray icon + menu
│   │   └── index.ts          # Point d'entrée + composition injeca
│   ├── preload/              # Scripts preload (contextBridge)
│   │   ├── index.ts          # Preload principal
│   │   ├── shared.ts         # Helpers preload (expose)
│   │   └── beat-sync.ts      # Preload beat-sync
│   ├── renderer/             # Process renderer (Vue 3)
│   │   ├── composables/      # Composables spécifiques renderer
│   │   ├── layouts/          # default.vue, stage.vue, settings.vue
│   │   ├── pages/            # index.vue, chat.vue, caption.vue, dashboard/, devtools/, about.vue...
│   │   ├── stores/           # Pinia stores renderer
│   │   │   ├── window.ts
│   │   │   ├── stage-window-lifecycle.ts
│   │   │   ├── chat-sync.ts
│   │   │   ├── stage-three-runtime-diagnostics.ts
│   │   │   ├── settings/server-channel*.ts
│   │   │   └── tools/builtin/{weather,widgets}.ts
│   │   ├── styles/           # CSS + UnoCSS resets
│   │   ├── main.ts           # Entrée Vue (createApp, plugins)
│   │   └── shims.d.ts        # Déclarations de types
│   └── shared/               # Contrats partagés main ↔ renderer
│       ├── eventa.ts         # Tous les contrats @moeru/eventa (295 lignes, 50+ events)
│       └── types/            # Types partagés
├── electron.vite.config.ts   # Config electron-vite (main/preload/renderer)
├── electron-builder.config.ts # Config electron-builder (signing, notarization, channels)
├── package.json
├── tsconfig.json
├── tsconfig.node.json
└── vitest.config.ts
```

### 4.1.3 Scripts package.json

```json
{
  "scripts": {
    "dev": "electron-vite dev",
    "typecheck:node": "tsc --noEmit -p tsconfig.node.json --composite false",
    "typecheck:web": "vue-tsc --noEmit -p tsconfig.web.json --composite false",
    "typecheck": "pnpm run typecheck:node && pnpm run typecheck:web",
    "build": "electron-vite build",
    "start": "electron-vite preview",
    "app:build": "pnpm run typecheck && pnpm run build && electron-builder",
    "app:build:win": "pnpm run typecheck && pnpm run build && electron-builder --win",
    "app:build:mac": "pnpm run typecheck && pnpm run build && electron-builder --mac",
    "app:build:linux": "pnpm run typecheck && pnpm run build && electron-builder --linux"
  }
}
```

### 4.1.4 `electron.vite.config.ts`

Trois entrées séparées :

1. **Main process** — Code-split `debug` et `h3` en chunks distincts pour éviter les side-effects. Sortie : `out/main/index.js`.
2. **Preload** — Deux cibles library : `index` (preload principal) et `beat-sync`. Sortie : `out/preload/{index,beat-sync}.mjs`.
3. **Renderer** — Deux HTML entrypoints (`main.html`, `beat-sync.html`) avec une chaîne de plugins Vite riche :
   - Vue Macros
   - unplugin-vue-router (routes auto depuis `src/renderer/pages/`)
   - vite-plugin-vue-layouts
   - UnoCSS
   - Vue I18n
   - @proj-airi/unplugin-fetch pour télécharger automatiquement les Live2D models et VRM samples
   - Aliases vers les packages internes

### 4.1.5 Composition Injeca (main/index.ts)

Le main process compose **14+ services** via `injeca.provide()`. L'ordre est déterminé par le graphe de dépendances. Liste complète (de `apps/stage-tamagotchi/src/main/index.ts:102-187`) :

| Clé | Dépendances | Rôle |
|-----|-------------|------|
| `configs:app` | — | Config globale (valibot) |
| `host:electron:app` | — | Instance Electron `app` |
| `services:auto-updater` | `appConfig` | electron-updater + channel persistence |
| `libs:i18n` | `appConfig` | i18n init avec locale persistée |
| `modules:channel-server` | `app`, `lifecycle` | Setup server-runtime embarqué |
| `modules:mcp-stdio-manager` | — | Gestionnaire MCP stdio |
| `modules:plugin-host` | `serverChannel` | Host plugins AIRI |
| `services:window-auth-manager` | — | OIDC window auth manager |
| `windows:beat-sync` | — | Fenêtre background audio |
| `windows:devtools:markdown-stress` | — | Devtools test rendering |
| `windows:onboarding` | `serverChannel`, `i18n`, `windowAuthManager` | Onboarding |
| `windows:notice` | `i18n`, `serverChannel` | Notices overlay |
| `windows:widgets` | `serverChannel`, `i18n` | Container widgets |
| `windows:about` | `autoUpdater`, `i18n`, `serverChannel` | About |
| `windows:chat` | `widgetsManager`, `serverChannel`, `mcpStdioManager`, `i18n` | Chat |
| `windows:settings` | `widgetsManager`, `beatSync`, `autoUpdater`, `devtoolsMarkdownStressWindow`, `serverChannel`, `mcpStdioManager`, `i18n`, `windowAuthManager` | Settings |
| `windows:main` | `settingsWindow`, `chatWindow`, `widgetsManager`, `noticeWindow`, `beatSync`, `autoUpdater`, `serverChannel`, `mcpStdioManager`, `i18n`, `onboardingWindowManager`, `windowAuthManager` | Fenêtre stage principale |
| `windows:caption` | `mainWindow`, `serverChannel`, `i18n` | Fenêtre sous-titres |
| `app:tray` | `mainWindow`, `settingsWindow`, `captionWindow`, `widgetsWindow`, `serverChannel`, `beatSyncBgWindow`, `aboutWindow`, `i18n` | Tray + menu |

### 4.1.6 Eventa IPC — les contrats principaux

Extrait de `src/shared/eventa.ts` (295 lignes) :

- **Windows & UI** : `electronOpenSettings`, `electronOpenChat`, `electronSettingsNavigate`, `electronStartDraggingWindow`, `electronWindowLifecycleChanged`
- **Server channel** : `electronGetServerChannelConfig`, `electronApplyServerChannelConfig`, `electronGetServerChannelQrPayload`
- **Auto-updater** : `electronGetUpdaterPreferences`, `electronSetUpdaterPreferences` (channels: `stable|alpha|beta|nightly|canary`)
- **Plugins** : `electronPluginList`, `electronPluginLoad`, `electronPluginUnload`, `electronPluginInspect`, `electronPluginUpdateCapability`
- **MCP** : `electronMcpApplyAndRestart`, `electronMcpGetRuntimeStatus`, `electronMcpListTools`, `electronMcpCallTool`
- **Widgets** : `widgetsAdd`, `widgetsRemove`, `widgetsFetch`, `widgetsUpdate`
- **Auth OIDC** : `electronAuthStartLogin`, `electronAuthCallback`, `electronAuthLogout`
- **Tracing** : `stageThreeRuntimeTraceForwardedEvent` (métriques VRM)

### 4.1.7 Distribution (electron-builder.config.ts)

- **App ID** : `ai.moeru.airi`
- **Nom** : `AIRI`
- **macOS** :
  - Hardened runtime + notarization
  - Entitlements mic/camera
  - Per-arch update channels : `latest-x64-mac.yml`, `latest-arm64-mac.yml`
- **Windows** :
  - NSIS installer
  - Per-arch : `latest-x64.yml`, `latest-arm64.yml`
- **Linux** :
  - Targets : deb, rpm
  - Per-arch channels

### 4.1.8 Tests

`vitest.config.ts` inclut `src/**/*.test.ts` et `scripts/**/*.test.ts`. 14 fichiers de test couvrent :
- Main : channel-server config, plugins, auto-updater, app lifecycle
- Renderer : window lifecycle, server channel, diagnostics, weather/widgets tools
- Scripts : génération de manifest d'update

---

## 4.2 `apps/stage-web` — PWA Web

### 4.2.1 Rôle

Application web déployée sur `airi.moeru.ai`. Offre la même UI que Tamagotchi mais dans un navigateur, avec l'expérience PWA (installable, offline-first).

### 4.2.2 Arborescence

```
stage-web/
├── public/                   # Assets publics (icons, manifest)
├── src/
│   ├── components/
│   ├── composables/          # audio-input, audio-record, icon-animation, perf/
│   ├── layouts/              # default, settings, stage
│   ├── modules/              # Module-level code
│   ├── pages/                # index.vue, auth/, settings/, devtools/, [...all].vue
│   ├── stores/               # background, pwa, devtools-lag
│   ├── styles/               # CSS + animations
│   ├── workers/              # Web workers
│   ├── main.ts               # Point d'entrée Vue
│   └── shims.d.ts
├── vite.config.ts            # Config Vite
├── package.json
├── tsconfig.json
└── index.html
```

### 4.2.3 Plugins Vite

Extrait de `vite.config.ts` :
- `VueRouter({ extensions: ['.vue', '.md'], routesFolder: [...] })` — routes auto depuis `src/pages/` et `packages/stage-pages/src/pages/`
- `vite-plugin-vue-layouts` (déprécié, sera remplacé par un loader natif Vue Router)
- `UnoCSS()`
- `VueI18n({ include: 'packages/i18n/**' })`
- `VueDevTools()`
- Mkcert (optionnel, pour HTTPS local avec `dev:https`)
- `vite-plugin-pwa` avec `registerType: 'prompt'`
- **WarpDrivePlugin** (`@proj-airi/vite-plugin-warpdrive`) pour uploader les assets lourds (VRM, Cubism SDK, fontes CJK) vers un bucket S3-compatible et réécrire les URLs en production.
- `@proj-airi/unplugin-fetch` pour télécharger les assets externes au build (Live2D SDK, samples)

### 4.2.4 Alias path

```typescript
resolve: {
  alias: {
    '@proj-airi/server-sdk': path.resolve('../../packages/server-sdk/src'),
    '@proj-airi/i18n':       path.resolve('../../packages/i18n/src'),
    '@proj-airi/stage-ui':   path.resolve('../../packages/stage-ui/src'),
    // etc.
  }
}
```

Les packages internes sont résolus directement depuis leur source TypeScript (pas de build préalable requis en mode dev).

### 4.2.5 `main.ts` de stage-web (schéma)

```typescript
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { MotionPlugin } from '@vueuse/motion'
import { autoAnimatePlugin } from '@formkit/auto-animate/vue'
import { createRouter, createWebHistory, createWebHashHistory } from 'vue-router'
import { setupLayouts } from 'virtual:generated-layouts'
import { routes } from 'vue-router/auto-routes'
import { createI18n } from 'vue-i18n'
import Tres from '@tresjs/core'

import messages from '@proj-airi/i18n/locales'
import App from './App.vue'

const app = createApp(App)

const router = createRouter({
  history: window.location.hostname.includes('huggingface.co')
    ? createWebHashHistory()
    : createWebHistory(),
  routes: setupLayouts(routes),
})

app.use(createPinia())
app.use(router)
app.use(MotionPlugin)
app.use(autoAnimatePlugin)
app.use(Tres)
app.use(createI18n({ legacy: false, locale: 'en', messages }))
app.mount('#app')
```

### 4.2.6 Scripts package.json

```json
{
  "scripts": {
    "dev": "vite",
    "dev:https": "vite --mode https",
    "build": "vite build",
    "preview": "vite preview",
    "typecheck": "vue-tsc --noEmit"
  }
}
```

### 4.2.7 Tests

Pas de `vitest.config.ts` ni de tests unitaires actuellement dans stage-web (la couverture se fait au niveau `packages/stage-ui`).

---

## 4.3 `apps/stage-pocket` — Mobile Capacitor

### 4.3.1 Rôle

Wrapper Capacitor autour de la même codebase Vue que stage-web, déployé sur iOS et Android. Ajoute une couche d'intégration native pour contourner les limitations des WebSockets sur certains WebView mobiles (notamment iOS).

### 4.3.2 Arborescence

```
stage-pocket/
├── android/                  # Projet Android natif (Gradle, Kotlin)
│   └── app/src/main/java/ai/moeru/airi_pocket/
│       ├── MainActivity.kt
│       └── websocket/
│           ├── HostWebSocketBridge.kt
│           ├── HostWebSocketClient.kt
│           ├── OkHttpHostWebSocketSessionFactory.kt
│           └── WebSocketBridgeJavascriptInterface.kt
├── ios/                      # Projet iOS natif (Xcode, Swift)
│   └── App/App/
│       ├── AppDelegate.swift
│       ├── DevBridgeViewController.swift
│       ├── HostWebSocketBridge.swift
│       └── URLSessionHostWebSocketSession.swift
├── src/
│   ├── modules/
│   │   ├── websocket-bridge.ts      # Bridge JS → native WebSocket
│   │   └── server-channel-qr-probe.ts # Probing multi-URL pour onboarding
│   └── ...                            # Partage des fichiers avec stage-web
├── capacitor.config.ts
├── vite.config.ts
├── package.json
└── keystore.properties       # (git-ignored) Signing Android
```

### 4.3.3 `capacitor.config.ts`

```typescript
import type { CapacitorConfig } from '@capacitor/cli'
import { env } from 'node:process'

const config: CapacitorConfig = {
  appId: env.CAP_TARGET === 'android'
    ? 'ai.moeru.airi_pocket'
    : 'ai.moeru.airi-pocket',
  appName: 'AIRI Pocket',
  webDir: 'dist',
  server: env.CAPACITOR_DEV_SERVER_URL
    ? { url: env.CAPACITOR_DEV_SERVER_URL, cleartext: true }
    : undefined,
  android: {
    buildOptions: {
      keystorePath: env.CAP_KEYSTORE_PATH,
      keystorePassword: env.CAP_KEYSTORE_PASSWORD,
      keystoreAlias: env.CAP_KEYSTORE_ALIAS,
      keystoreAliasPassword: env.CAP_KEYSTORE_ALIAS_PASSWORD,
      releaseType: 'APK',
    },
  },
}

export default config
```

### 4.3.4 Vite config spécifique

- Étend celle de stage-web mais :
  - `server.host: '0.0.0.0'`, `server.port: 5273`
  - Exclut la page `settings/connection/index.vue` (non pertinente sur mobile)
  - Plugin `proj-airi:defines` qui définit :
    - `RUNTIME_ENVIRONMENT = 'capacitor'`
    - `URL_MODE = 'server'` (dev) ou `'file'` (prod)
  - Si `VITE_CAP_SYNC_IOS_AFTER_BUILD`, synchronise avec iOS après le build

### 4.3.5 Le bridge WebSocket

**Problème** : sur certaines versions iOS, `WebSocket` natif du WebView ne supporte pas bien les connexions longue durée, et surtout n'est pas accessible en arrière-plan.

**Solution** : on implémente une classe `HostWebSocket` compatible avec l'interface `WebSocket` standard, qui route ses appels vers une couche Swift/Kotlin via `window.webkit.messageHandlers.airiHostBridge` (iOS) ou `window.AiriHostBridge` (Android).

**Fichier clé** : `src/modules/websocket-bridge.ts`

```typescript
export class HostWebSocket implements WebSocketLike {
  readyState = WebSocket.CONNECTING

  constructor(url: string) {
    // Stocke l'ID d'instance pour router les messages
    const instanceId = nanoid()
    this.instanceId = instanceId

    // Demande au natif de connecter
    if (isAndroid()) {
      window.AiriHostBridge.connect(instanceId, url)
    } else if (isIOS()) {
      window.webkit.messageHandlers.airiHostBridge.postMessage({
        action: 'connect', instanceId, url,
      })
    }
  }

  send(data: string | ArrayBuffer) { /* route via native */ }
  close() { /* route via native */ }
  // onopen, onmessage, onclose, onerror sont appelés depuis le natif
  // via window.dispatchNativeMessage(instanceId, eventType, payload)
}
```

Ensuite, le client `server-sdk` est construit avec ce constructeur custom :

```typescript
new Client({
  name: 'proj-airi:stage-pocket',
  url: serverUrl,
  websocketConstructor: HostWebSocket,
})
```

### 4.3.6 Le probing QR

**Fichier** : `src/modules/server-channel-qr-probe.ts`

Lors du scan d'un QR code d'onboarding, on obtient une liste d'URLs candidates (ex: `192.168.1.10:6121`, `192.168.1.11:6121`, `localhost:6121`). Le probe essaie chacune avec un timeout de **2.5s** et retourne la première qui répond.

### 4.3.7 Côté natif iOS

`HostWebSocketBridge.swift` utilise `URLSessionWebSocketTask` :

```swift
class URLSessionHostWebSocketSession {
  private var task: URLSessionWebSocketTask
  private let handler: WKScriptMessageHandler

  func connect(url: URL) {
    self.task = URLSession.shared.webSocketTask(with: url)
    self.task.resume()
    self.listen()
  }

  private func listen() {
    task.receive { result in
      switch result {
        case .success(.string(let text)):
          self.forwardToJS("message", text)
        case .success(.data(let data)):
          // ...
        case .failure(let error):
          self.forwardToJS("error", error.localizedDescription)
      }
      self.listen()
    }
  }
}
```

### 4.3.8 Côté natif Android

`HostWebSocketBridge.kt` utilise OkHttp :

```kotlin
class HostWebSocketBridge(private val webView: WebView) {
  private val sessions = mutableMapOf<String, WebSocket>()

  @JavascriptInterface
  fun connect(instanceId: String, url: String) {
    val request = Request.Builder().url(url).build()
    val ws = OkHttpClient().newWebSocket(request, object : WebSocketListener() {
      override fun onOpen(ws: WebSocket, response: Response) {
        forwardToJS(instanceId, "open", "")
      }
      override fun onMessage(ws: WebSocket, text: String) {
        forwardToJS(instanceId, "message", text)
      }
      // ...
    })
    sessions[instanceId] = ws
  }
}
```

### 4.3.9 Tests Android

Des tests JUnit existent dans `androidTest/` et `test/` :
- `MainActivityBridgeTest.kt`
- `HostWebSocketBridgeTest.kt`

---

## 4.4 `apps/server` — Serveur SaaS multi-user

### 4.4.1 Rôle

Serveur Hono destiné aux déploiements multi-utilisateurs (hébergés). Fournit :
- Authentification OAuth/OIDC (better-auth)
- Gestion des utilisateurs, personnages, chats
- Fournisseurs LLM configurables par utilisateur
- Système de crédits (Flux) + facturation Stripe
- Endpoint OpenAI-compatible (`/openai/v1`)
- WebSocket pour chat temps réel
- OpenTelemetry (traces + logs + métriques)

### 4.4.2 Arborescence

```
server/
├── src/
│   ├── app.ts                # Setup Hono + DI
│   ├── bin/run.ts            # CLI (role = api | billing-consumer)
│   ├── routes/               # auth, characters, chats, flux, providers, openai, stripe, oidc
│   ├── services/             # Business logic
│   ├── db/                   # Schémas Drizzle
│   ├── middleware/           # Auth, CORS, logging, tracing
│   └── ...
├── drizzle/                  # Migrations SQL
├── package.json
└── drizzle.config.ts
```

### 4.4.3 Scripts

```json
{
  "scripts": {
    "dev": "tsx --env-file .env src/bin/run.ts api",
    "start": "node dist/bin/run.js api",
    "server": "node dist/bin/run.js api",
    "db:generate": "drizzle-kit generate",
    "db:push": "drizzle-kit push",
    "auth:generate": "npx @better-auth/cli generate"
  }
}
```

### 4.4.4 Technologies

- **Hono** 4.11 (framework HTTP)
- **@hono/node-ws** pour WebSocket intégré
- **Drizzle ORM** + PostgreSQL
- **Redis** (cache, sessions)
- **better-auth** 1.5 (OAuth + OIDC + passkeys)
- **Stripe** pour paiements
- **injeca** pour la DI (même pattern que le main process Electron)
- **OpenTelemetry** (`@opentelemetry/sdk-node`) exporté en OTLP

---

## 4.5 `apps/ui-server-auth` — UI Authentification

### 4.5.1 Rôle

Frontend Vue minimaliste pour les pages d'authentification de `apps/server` (login, callback, profile). Déployé séparément pour pouvoir être hébergé sur un sous-domaine distinct.

### 4.5.2 Stack

- Vite 8 + Vue 3 + Vue Router + Pinia
- Sonner (toast), Vaul Vue (drawer), @vueuse/core
- PostHog (analytics)
- Internationalisation via `@proj-airi/i18n`

### 4.5.3 Tests

Inclus dans le projet vitest racine (`apps/ui-server-auth` est listé dans `vitest.config.ts`).

---

## 4.6 `apps/component-calling` — Expérimental

Petit projet de démo / expérimentation autour des appels de composants. Non détaillé ici — voir le README interne si présent.
