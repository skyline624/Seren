# 02 — Architecture globale

> **Note Seren.** Ce chapitre documente AIRI comme implémentation de référence.
> Seren reprend le protocole WebSocket et le split *stage / server* mais **n'embarque
> qu'un seul renderer d'avatar : Live2D Cubism 4** via `pixi-live2d-display`.
> Toutes les mentions « VRM / Live2D » ci-dessous décrivent la flexibilité d'AIRI,
> pas la stack Seren ; voir `README.md` § *Avatar animation pipeline* pour l'état
> effectif du renderer côté Seren.

## 2.1 Vue aérienne : le split *stage / server*

L'architecture d'AIRI est fondée sur une séparation nette entre deux couches :

- **Stage** (« la scène ») — la surface frontale visible par l'utilisateur. Trois apps la matérialisent (`stage-web`, `stage-tamagotchi`, `stage-pocket`), toutes bâties sur le même noyau UI (`packages/stage-ui`). Elles rendent l'avatar (VRM ou Live2D), affichent les sous-titres, et proposent l'interface de configuration.
- **Server channel** (« le canal serveur ») — le runtime backend qui orchestre les appels LLM, la mémoire, les outils et les flux audio. Il est implémenté par `packages/server-runtime` et exposé aux clients via `packages/server-sdk`. Dans la distribution desktop, ce runtime est **embarqué dans le main process Electron**. Dans la distribution web, il tourne comme service séparé (via `apps/server` ou une instance locale).

```
┌───────────────────────────────────────────────────────────────┐
│                         STAGE (frontend)                      │
│                                                               │
│  ┌──────────────┐   ┌──────────────────┐   ┌──────────────┐   │
│  │  stage-web   │   │ stage-tamagotchi │   │ stage-pocket │   │
│  │  (PWA Vue)   │   │  (Electron Vue)  │   │  (Capacitor) │   │
│  └──────┬───────┘   └────────┬─────────┘   └──────┬───────┘   │
│         │                    │                    │           │
│         └──────── packages/stage-ui ───────────────┘           │
│                   packages/stage-shared                        │
│                   packages/stage-layouts                       │
│                   packages/stage-pages                         │
│                   packages/i18n                                │
└───────────────────┬───────────────────────────────────────────┘
                    │
                    │  WebSocket (superjson-encoded enveloppe)
                    │  Protocole défini par @proj-airi/plugin-protocol
                    │
┌───────────────────▼───────────────────────────────────────────┐
│                  SERVER-RUNTIME (cœur backend)                │
│                                                               │
│   ┌──────────────────────────────────────────────────────┐    │
│   │        packages/server-runtime (H3 + crossws)        │    │
│   │  ┌────────────────────────────────────────────────┐  │    │
│   │  │ Registry: Map<peerId, AuthenticatedPeer>      │  │    │
│   │  │ Consumer registry (broadcast / consumer /     │  │    │
│   │  │   consumer-group + first/rr/priority/sticky)  │  │    │
│   │  │ Heartbeat + health monitor (miss threshold)   │  │    │
│   │  │ Rate limiter par peer (sliding window)        │  │    │
│   │  │ Origin/CORS checker                            │  │    │
│   │  │ Routing middleware pipeline                    │  │    │
│   │  └────────────────────────────────────────────────┘  │    │
│   └──────────────────────────────────────────────────────┘    │
│                                                               │
└───────────────────┬───────────────────────────────────────────┘
                    │
                    │  Les services, plugins et bots se connectent
                    │  au serveur en tant que « modules » (SDK)
                    │
┌───────────────────▼───────────────────────────────────────────┐
│                       MODULES CONNECTÉS                      │
│                                                               │
│  services/        plugins/                packages/          │
│  ┌──────────┐    ┌────────────────┐      ┌────────────────┐   │
│  │discord-  │    │airi-plugin-    │      │audio-pipelines │   │
│  │bot       │    │llm-orchestrator│      │-transcribe     │   │
│  │          │    │                │      │                │   │
│  │minecraft │    │airi-plugin-    │      │model-driver-   │   │
│  │          │    │claude-code     │      │lipsync         │   │
│  │telegram- │    │                │      │                │   │
│  │bot       │    │airi-plugin-    │      │model-driver-   │   │
│  │          │    │homeassistant   │      │mediapipe       │   │
│  │satori-bot│    │                │      │                │   │
│  │          │    │airi-plugin-    │      │memory-pgvector │   │
│  │twitter-  │    │web-extension   │      │(WIP)           │   │
│  │services  │    │                │      │                │   │
│  └──────────┘    │airi-plugin-    │      │plugin-sdk      │   │
│                  │bilibili-laplace│      │                │   │
│                  └────────────────┘      └────────────────┘   │
└───────────────────────────────────────────────────────────────┘
```

Cette architecture en « hub and spoke » signifie que :
- Les **applications frontales** ne parlent jamais directement aux bots ou aux LLMs.
- Tout passe par le **hub (server-runtime)** qui route les événements selon des règles (broadcast, consumer, consumer-group).
- Un module peut s'ajouter ou disparaître à chaud sans redéployer les autres.

## 2.2 Modèle de déploiement

### Mode « tout-en-un desktop »

L'application `stage-tamagotchi` embarque un serveur `server-runtime` dans son main process Electron. Le renderer (UI Vue) se connecte alors à `ws://localhost:<port>/ws` via le SDK. Les plugins et services tournent en tant que sous-processus ou modules WASM/Node chargés dans le main process, selon leur nature.

```
┌─────────────────────────────────────────────┐
│            Electron App (Tamagotchi)        │
│                                             │
│   ┌──────────────┐   ┌───────────────────┐  │
│   │  Main proc   │◄──┤ server-runtime    │  │
│   │  (injeca DI) │   │ embarqué          │  │
│   └──────┬───────┘   └───────────────────┘  │
│          │                    ▲              │
│          │ eventa IPC         │ WS           │
│          ▼                    │              │
│   ┌──────────────┐    ┌───────┴────────┐     │
│   │  Renderer    │────┤ server-sdk     │     │
│   │  (Vue 3)     │    │ Client         │     │
│   └──────────────┘    └────────────────┘     │
│                                             │
└─────────────────────────────────────────────┘
```

### Mode « client distant »

L'application `stage-web` ou `stage-pocket` se connecte à un `server-runtime` distant ou à un autre poste (machine locale avec tamagotchi, serveur VPS…). Le code d'**onboarding** (`packages/stage-shared/src/server-channel-qr.ts` + `apps/stage-pocket/src/modules/server-channel-qr-probe.ts`) gère la découverte de l'URL via QR code.

```
┌────────────┐      WebSocket      ┌────────────────────┐
│ stage-web  │ ─────────────────► │ server-runtime     │
│ (navigateur│                    │ (Node ou embarqué  │
│  ou mobile)│ ◄──────────────── │  dans Tamagotchi)  │
└────────────┘                    └────────────────────┘
```

### Mode « hébergé » (apps/server)

`apps/server` est une distribution distincte pour les déploiements multi-utilisateurs, avec auth OIDC (better-auth), Redis, PostgreSQL, Stripe, et un endpoint OpenAI-compatible. Elle ré-expose certains services d'AIRI comme un SaaS.

## 2.3 Flux de données principal : le chat vocal complet

Le cas d'usage canonique d'AIRI est un **échange vocal** de bout en bout. Voici la séquence sur le mode tout-en-un desktop :

```
┌───────────┐
│Micro  AudioWorklet capture (48k)
└─────┬─────┘
      │ Float32Array chunks
      ▼
┌─────────────────────────┐
│ VAD Worker              │ ── packages/stage-ui/workers/vad
│ (Silero, ONNX)          │
└─────┬───────────────────┘
      │ speech start / end
      ▼
┌─────────────────────────┐
│ Speech pipeline         │ ── packages/pipelines-audio
│ (events via eventa)     │
└─────┬───────────────────┘
      │ Blob audio
      ▼
┌─────────────────────────┐
│ Transcription provider  │ ── stores/providers + @xsai/generate-transcription
│ (OpenAI, local Whisper) │
└─────┬───────────────────┘
      │ text
      ▼
┌─────────────────────────────────────┐
│ Client.send({                       │
│   type: 'input:text:voice',         │
│   data: { transcription, source }   │
│ })                                  │ ── @proj-airi/server-sdk
└─────┬───────────────────────────────┘
      │ superjson → WebSocket
      ▼
┌─────────────────────────────────────┐
│ server-runtime dispatcher           │ ── packages/server-runtime
│  • envelope validation              │
│  • rate limiting                    │
│  • routing middleware               │
│  • consumer selection               │
└─────┬───────────────────────────────┘
      │ délivré aux modules abonnés
      ▼
┌─────────────────────────────────────┐
│ LLM orchestrator                    │ ── plugins/airi-plugin-llm-orchestrator
│  • assemble contexte                │    ou module « consciousness »
│  • stream-text via @xsai/stream-text│    ── stores/modules/consciousness.ts
│  • parse markers (émotions,actions) │
└─────┬───────────────────────────────┘
      │ chunks tokens + markers
      ▼
┌─────────────────────────────────────┐
│ Response categoriser                │ ── composables/use-chat-session
│  + TTS queue (Kokoro / ElevenLabs)  │
└─────┬───────────────────────────────┘
      │ Float32Array + viseme frames
      ▼
┌─────────────────────────────────────┐
│ Playback manager + lipsync          │ ── pipelines-audio/playback
│  Audio output + param mouth drive   │    model-driver-lipsync
└─────┬───────────────────────────────┘
      │ paramètres / expressions
      ▼
┌─────────────────────────────────────┐
│ VRM / Live2D renderer               │ ── stage-ui-three / stage-ui-live2d
│  mouth + expressions + animations   │
└─────────────────────────────────────┘
```

Chaque étape utilise le même bus d'événements (`@moeru/eventa`) ou des canaux dédiés (Worker messages, AudioWorklet ports), ce qui permet de remplacer n'importe quel composant (ex: STT cloud → STT local) sans toucher au reste.

## 2.4 Flux de données « texte » simple

Quand l'utilisateur tape du texte dans la fenêtre de chat :

```
User keystroke
      ▼
Chat.vue (composant UI) — stores/chat/messages
      ▼
Client.send({ type: 'input:text', data: { text, source: { 'stage-tamagotchi': true } } })
      ▼
server-runtime  ──►  llm-orchestrator (consumer de 'input:text')
      ▼
stream-text (xsai)  ──►  chunks via 'output:gen-ai:chat:message'
      ▼
stage-tamagotchi (consumer)  ──►  affichage progressif + TTS + animation
```

## 2.5 Flux d'intégration externe (ex: Discord)

Quand un utilisateur envoie un message dans un serveur Discord :

```
Discord server → discord.js client (services/discord-bot)
      ▼
adapter : messageCreate event
      ▼
Client.send({ type: 'input:text', data: { text, source: { discord: { guildId, channelId, member } } } })
      ▼
server-runtime  ──►  llm-orchestrator
      ▼
'output:gen-ai:chat:message' broadcast
      ▼
discord-bot (consumer) reçoit la réponse et
  • synthétise la voix (@xsai/generate-speech)
  • joue dans le salon vocal (@discordjs/voice)
  • ou poste dans le channel texte
```

Le même pattern s'applique à Telegram, Satori, Minecraft (`spark:command` events), Twitter/X, etc.

## 2.6 Découplage par le protocole

Tous les modules échangent des objets typés du genre :

```typescript
interface WebSocketBaseEvent<T, D> {
  type: T                    // ex: 'input:text', 'module:announce'
  data: D                    // charge utile typée
  metadata: {
    source: ModuleIdentity   // qui a émis (plugin-id + instance-id)
    event: { id: string, parentId?: string }
  }
  route?: RouteConfig        // destinations + delivery override
}
```

Les contrats sont définis dans `packages/plugin-protocol/src/types/events.ts` (plus de 1400 lignes) et sont exportés vers tout le monde. Ni les apps, ni les services, ni les plugins n'ont besoin de partager du code — ils partagent des **types** et un **schéma de messages**.

## 2.7 Lifecycle d'un module

Tout module (frontend, service, plugin) suit ce cycle :

```
1. Connexion WebSocket
2. module:authenticate (si token requis)
   ◄ module:authenticated
3. module:announce {name, identity, possibleEvents, dependencies, configSchema}
   ◄ module:announced
4. registry:modules:sync (reçu du serveur : qui d'autre est là)
5. (optionnel) module:prepared
6. (optionnel) module:configuration:* (validate, plan, commit)
7. (optionnel) module:contribute:capability:offer
8. module:status (phase = 'ready')
9. Boucle : send/receive événements applicatifs
10. Fermeture : module:de-announced (implicite à la déco)
```

## 2.8 Schéma de connexion inter-process Electron

Dans `stage-tamagotchi`, en plus du canal WebSocket (renderer ↔ server-runtime), il y a un second canal **eventa IPC** entre main process et renderer pour les opérations OS (ex: redimensionner la fenêtre, ouvrir un menu, lire un préférence système). Ces contrats sont dans `apps/stage-tamagotchi/src/shared/eventa.ts`.

```
Renderer Vue  ──► @moeru/eventa.invoke('electron.window.setBounds', {...})
                        ▼
                  preload (exposeInvokeHandler)
                        ▼
                  Main process → defineInvokeHandler('electron.window.setBounds')
                        ▼
                  BrowserWindow.setBounds(...)
```

## 2.9 Points d'extension

Les trois points d'extension principaux sont :

1. **Créer un nouveau module** : écrire un service Node qui utilise `Client` du `server-sdk`, annonce ses `possibleEvents` et expose son `configSchema`.
2. **Créer un nouveau plugin** : utiliser `@proj-airi/plugin-sdk` + `@proj-airi/plugin-protocol` pour bénéficier du cycle de vie complet (capability offer, permission grants, reload à chaud).
3. **Ajouter un fournisseur LLM/TTS** : implémenter une définition standardisée dans `packages/stage-ui/src/stores/providers/<provider-name>/` qui expose un client compatible avec l'interface Provider.
