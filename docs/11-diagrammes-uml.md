# 11 — Diagrammes UML & schémas

Ce document rassemble les diagrammes UML et schémas d'architecture d'AIRI. Les diagrammes sont au format **Mermaid** pour être rendus directement par la plupart des outils (GitHub, VS Code, Obsidian).

## 11.1 Diagramme de déploiement

```mermaid
graph TB
    subgraph Desktop["Desktop Tamagotchi (Electron)"]
        TM[Main Process]
        TR[Renderer Process]
        TS[server-runtime embarqué]
        TM ---|injeca DI| TS
        TM ---|eventa IPC| TR
        TR ---|server-sdk WS| TS
    end

    subgraph Web["Web PWA"]
        WB[Navigateur Vue]
    end

    subgraph Mobile["Mobile Pocket (Capacitor)"]
        MW[WebView Vue]
        MN[Native Bridge Swift/Kotlin]
        MW ---|JS Bridge| MN
    end

    subgraph Cloud["Cloud / LAN"]
        CS[server-runtime Node]
        AS[apps/server (Hono)]
        DB[(PostgreSQL)]
        RD[(Redis)]
    end

    subgraph External["Services externes"]
        DC[Discord]
        TG[Telegram]
        MC[Minecraft server]
        TX[Twitter/X]
        BL[Bilibili Live]
        LLM[LLM providers]
        TTS[TTS providers]
    end

    WB ---|WS direct| CS
    MN ---|WS via native| CS

    CS ---|consumer pattern| DB

    AS ---|auth/chat/flux| DB
    AS ---|sessions| RD

    subgraph Bots
        DB2[discord-bot]
        TB[telegram-bot]
        MB[minecraft-bot]
        TWB[twitter-services]
        BB[bilibili-laplace]
    end

    DB2 -.WS.-> CS
    TB -.WS.-> CS
    MB -.WS.-> CS
    TWB -.WS.-> CS
    BB -.WS.-> CS

    DB2 --> DC
    TB --> TG
    MB --> MC
    TWB --> TX
    BB --> BL

    subgraph LLMPluginHost
        LO[airi-plugin-llm-orchestrator]
    end

    LO -.WS.-> CS
    LO --> LLM
    TS --> LLM
    TS --> TTS
```

## 11.2 Diagramme de classes (server-runtime ↔ server-sdk)

```mermaid
classDiagram
    class Client {
        -websocket: WebSocketLike
        -shouldClose: boolean
        -status: ClientStatus
        -heartbeatTimer: Interval
        -eventListeners: Map
        -identity: MetadataEventSource
        -heartbeat: ClientHeartbeatOptions
        +constructor(opts: ClientOptions)
        +connect(opts) Promise~void~
        +ready(opts) Promise~void~
        +onEvent(type, cb) Unsubscribe
        +offEvent(type, cb)
        +send(event) boolean
        +sendOrThrow(event)
        +sendRaw(data) boolean
        +close()
        +onConnectionStateChange(cb)
        +isReady() boolean
        +isSocketOpen() boolean
        +connectionStatus() ClientStatus
    }

    class ClientOptions {
        +name: string
        +url?: string
        +token?: string
        +websocketConstructor?: WebSocketLikeConstructor
        +identity?: MetadataEventSource
        +possibleEvents?: string[]
        +dependencies?: ModuleDependency[]
        +configSchema?: ModuleConfigSchema
        +heartbeat?: ClientHeartbeatOptions
        +autoConnect?: boolean
        +autoReconnect?: boolean
        +maxReconnectAttempts?: number
        +onError?: fn
        +onClose?: fn
        +onReady?: fn
        +onStateChange?: fn
    }

    Client --> ClientOptions : uses

    class ClientStatus {
        <<enumeration>>
        idle
        connecting
        authenticating
        announcing
        ready
        closing
    }

    Client --> ClientStatus : has

    class ServerRuntime {
        -peers: Map~string, AuthenticatedPeer~
        -peersByModule: Map
        -consumers: ConsumerRegistry
        -healthMonitor: Interval
        -rateLimiter: RateLimiter
        -middlewares: RouteMiddleware[]
        +setupApp(opts) H3App
        +createServer(opts) Server
        +detectHeartbeatControlFrame(text)
        +resolveDeliveryConfig(event)
    }

    class AuthenticatedPeer {
        +peer: Peer
        +authenticated: boolean
        +name?: string
        +index?: number
        +identity?: ModuleIdentity
        +lastHeartbeatAt: number
        +healthy: boolean
        +missedHeartbeats: number
    }

    ServerRuntime --> AuthenticatedPeer : tracks

    class WebSocketBaseEvent {
        +type: T
        +data: D
        +metadata: EventMetadata
        +route?: RouteConfig
    }

    Client ..> WebSocketBaseEvent : dispatches
    ServerRuntime ..> WebSocketBaseEvent : routes

    class ModuleIdentity {
        +id: string
        +kind: 'plugin'
        +plugin: PluginIdentity
        +labels?: Record
    }

    WebSocketBaseEvent --> ModuleIdentity : metadata.source
```

## 11.3 Diagramme de séquence : connexion d'un module

```mermaid
sequenceDiagram
    participant M as Module (Client SDK)
    participant WS as WebSocket
    participant SR as server-runtime
    participant P as Peer registry

    M->>WS: new WebSocket(url)
    WS->>SR: upgrade request
    SR->>SR: Origin check
    alt Origin invalid
        SR-->>WS: 403 Forbidden
    else Origin valid
        SR-->>WS: 101 Switching Protocols
        SR->>P: create Peer state (authenticated=false si token)
    end

    alt token required
        M->>SR: {type:'module:authenticate', data:{token}}
        SR->>SR: timingSafeCompare(token, expected)
        alt invalid
            SR-->>M: {type:'error', data:{message:'Invalid token'}}
            SR->>WS: close
        else valid
            SR->>P: set authenticated=true
            SR-->>M: {type:'module:authenticated', data:{authenticated:true}}
            SR-->>M: {type:'registry:modules:sync', data:{modules:[...]}}
        end
    else no token
        SR-->>M: {type:'registry:modules:sync', data:{modules:[...]}}
    end

    M->>SR: {type:'module:announce', data:{name, identity, possibleEvents, ...}}
    SR->>P: register peer.name, peer.identity, peer.index
    SR-->>M: {type:'module:announced', data:{name, index, identity}}
    SR->>P: broadcast module:announced to all peers

    Note over M,SR: Module now ready to send/receive events

    loop every pingInterval
        M->>SR: {type:'transport:connection:heartbeat', data:{kind:'ping'}}
        SR->>P: update lastHeartbeatAt
        SR-->>M: {type:'transport:connection:heartbeat', data:{kind:'pong'}}
    end
```

## 11.4 Diagramme de séquence : input vocal complet

```mermaid
sequenceDiagram
    actor U as Utilisateur
    participant MIC as Micro (AudioWorklet)
    participant VAD as VAD Worker
    participant STT as STT Provider
    participant C as server-sdk Client
    participant SR as server-runtime
    participant LLM as llm-orchestrator
    participant TTS as TTS Worker (Kokoro)
    participant PM as Playback Manager
    participant VRM as VRM Renderer

    U->>MIC: parle
    MIC->>VAD: Float32Array chunks
    VAD->>VAD: détection voix
    VAD-->>MIC: speech-start event
    U->>MIC: continue parler
    VAD->>MIC: speech-end + audio buffer
    MIC->>STT: transcribe(audioBuffer)
    STT-->>MIC: { text: "Hello AIRI" }
    MIC->>C: {type:'input:text:voice', data:{transcription, 'stage-tamagotchi':true}}
    C->>SR: superjson.stringify(event)
    SR->>SR: validate + rate limit + route
    SR->>LLM: deliver to consumer (llm-orchestrator)
    LLM->>LLM: assemble context, streamText()
    loop for each chunk
        LLM->>SR: {type:'output:gen-ai:chat:message:chunk', data:{chunk}}
        SR->>C: deliver
        C->>TTS: synthesize(chunkText)
        TTS-->>PM: Float32Array + viseme frames
        PM->>VRM: paramètres mouth + expressions
        VRM->>U: affichage (audio + animation)
    end
    LLM->>SR: {type:'output:gen-ai:chat:message:end'}
    SR->>C: deliver
```

## 11.5 Diagramme d'état : Client SDK connection state

```mermaid
stateDiagram-v2
    [*] --> idle

    idle --> connecting : connect()
    connecting --> authenticating : socket open && token
    connecting --> announcing : socket open && !token
    connecting --> idle : error + shouldReconnect
    connecting --> closing : close()

    authenticating --> announcing : module:authenticated
    authenticating --> idle : invalid token (terminal)

    announcing --> ready : module:announced

    ready --> closing : close()
    ready --> idle : socket close + autoReconnect + backoff

    closing --> idle : socket closed

    idle --> [*]
```

## 11.6 Diagramme de composants : stage-tamagotchi

```mermaid
graph LR
    subgraph Main["Main process (injeca DI)"]
        MI[index.ts]
        MI --> C[configs:app]
        MI --> UA[services:auto-updater]
        MI --> I18N[libs:i18n]
        MI --> CS[modules:channel-server]
        MI --> MCP[modules:mcp-stdio-manager]
        MI --> PH[modules:plugin-host]
        MI --> WA[services:window-auth-manager]

        MI --> BS[windows:beat-sync]
        MI --> ON[windows:onboarding]
        MI --> NO[windows:notice]
        MI --> WG[windows:widgets]
        MI --> AB[windows:about]
        MI --> CH[windows:chat]
        MI --> ST[windows:settings]
        MI --> MW[windows:main]
        MI --> CA[windows:caption]
        MI --> TR[app:tray]
    end

    subgraph Preload["Preload (contextBridge)"]
        PI[index.ts]
        PB[beat-sync.ts]
    end

    subgraph Renderer["Renderer (Vue 3)"]
        RM[main.ts]
        RR[Vue Router]
        RS[Pinia stores]
        RC[Composables]

        RM --> RR
        RM --> RS
        RS --> RC
    end

    MW -- contextBridge --> PI --> Renderer
    BS -- contextBridge --> PB

    CS -.eventa.-> RS
    Renderer -.server-sdk.-> CS
```

## 11.7 Diagramme de composants : stage-ui (cœur UI)

```mermaid
graph TB
    subgraph Components
        SC[scenarios/]
        SCN[scenes/]
        GD[gadgets/]
        MD[markdown/]
        MOD[modules/]
    end

    subgraph Composables
        ACH[audio/]
        VSC[vision/]
        CHS[use-chat-session/]
        LMP[llm-marker-parser]
        RCA[response-categoriser]
    end

    subgraph Stores
        PROV[providers/]
        MODS[modules/]
        CHT[chat/]
        CHR[character/]
        AI[ai/]
    end

    subgraph Workers
        VAD[vad/]
        KOK[kokoro/]
    end

    subgraph External["Types externes"]
        PP[plugin-protocol/events]
    end

    Components --> Composables
    Components --> Stores
    Composables --> Stores
    Stores --> PP
    MODS --> PROV
    ACH --> Workers
    CHS --> Workers

    MODS -->|consciousness| LLM[xsai streamText]
    MODS -->|hearing| STT[xsai generate-transcription]
    MODS -->|speech| TTS[xsai generate-speech / Kokoro]
```

## 11.8 Diagramme de séquence : handshake handshake Electron IPC (eventa)

```mermaid
sequenceDiagram
    participant R as Renderer
    participant P as Preload
    participant M as Main (handler)

    Note over R,M: Phase 1 : définition du contrat dans src/shared/eventa.ts

    R->>P: invoke(electronGetWindowBounds, {windowName: 'main'})
    P->>P: __AIRI_EVENTA__.invoke(id, payload)
    P->>M: ipcRenderer.invoke(channel, {id, payload})
    M->>M: registered handler for 'electron.window.getBounds'
    M->>M: getWindowByName('main').getBounds()
    M-->>P: { x, y, width, height }
    P-->>R: Promise resolve
```

## 11.9 Diagramme de séquence : onboarding QR (client pocket vers runtime local)

```mermaid
sequenceDiagram
    actor U as Utilisateur
    participant PT as stage-pocket (Android/iOS)
    participant SCAN as Barcode Scanner
    participant QR as QR Probe
    participant SR as server-runtime (distant)

    U->>PT: scan QR code
    PT->>SCAN: capture caméra
    SCAN-->>PT: QR payload (JSON)
    PT->>QR: parseQrPayload(data)
    QR-->>PT: [{url1, url2, url3}, token?]

    par Probing en parallèle
        PT->>SR: GET url1 (timeout 2.5s)
        SR-->>PT: 200 or timeout
    and
        PT->>SR: GET url2
        SR-->>PT: 200 or timeout
    and
        PT->>SR: GET url3
        SR-->>PT: 200 or timeout
    end

    alt At least one URL responded
        PT->>PT: select first valid url
        PT->>PT: create Client({url, token, websocketConstructor: HostWebSocket})
        PT->>SR: WebSocket connection
        Note over PT,SR: Classical handshake module:authenticate / module:announce
    else All failed
        PT-->>U: Error : unable to connect
    end
```

## 11.10 Diagramme d'activités : cycle de vie d'un plugin

```mermaid
flowchart TB
    Start([Start]) --> Connect[Client.connect]
    Connect --> AuthNeeded{Token?}
    AuthNeeded -->|Yes| Auth[module:authenticate]
    AuthNeeded -->|No| Announce[module:announce]
    Auth --> AuthOk{Valid?}
    AuthOk -->|No| End([Close connection])
    AuthOk -->|Yes| Announce

    Announce --> Sync[Receive registry:modules:sync]
    Sync --> Prepare[module:prepared]
    Prepare --> DepsOk{Deps met?}
    DepsOk -->|No| WaitDeps[Wait for dependency modules]
    WaitDeps --> DepsOk
    DepsOk -->|Yes| ConfigNeeded{Needs config?}

    ConfigNeeded -->|Yes| ConfigLoop[module:configuration:needed]
    ConfigLoop --> ConfigPlan[receive module:configuration:plan:request]
    ConfigPlan --> ConfigValidate[receive module:configuration:validate:request]
    ConfigValidate --> ConfigCommit[receive module:configuration:commit]
    ConfigCommit --> Configured[module:configuration:configured]
    ConfigNeeded -->|No| Configured

    Configured --> OfferCaps[module:contribute:capability:offer for each capability]
    OfferCaps --> Ready[module:status phase=ready]

    Ready --> Loop[Event loop send/receive]
    Loop --> Heartbeat[Heartbeat]
    Heartbeat --> Loop
    Loop --> Close{close triggered?}
    Close -->|No| Loop
    Close -->|Yes| DeAnnounce[module:de-announced]
    DeAnnounce --> End
```

## 11.11 Diagramme de déploiement Electron (multi-fenêtres)

```mermaid
graph TB
    subgraph ElectronApp["Electron App Process"]
        M[Main Process]
        subgraph Windows
            WM[main window]
            WS[settings]
            WC[chat]
            WCap[caption]
            WW[widgets container]
            WA[about]
            WO[onboarding]
            WN[notice]
            WB[beat-sync background]
            WDev[devtools markdown stress]
        end
        M -.creates.-> WM
        M -.creates.-> WS
        M -.creates.-> WC
        M -.creates.-> WCap
        M -.creates.-> WW
        M -.creates.-> WA
        M -.creates.-> WO
        M -.creates.-> WN
        M -.creates.-> WB
        M -.creates.-> WDev

        subgraph Services["Services (injeca)"]
            SCS[channel-server]
            SAU[auto-updater]
            SWA[window-auth]
            SPH[plugin-host]
            SMCP[mcp-stdio]
            STI[tray]
            SI[i18n]
            SCF[configs]
        end

        M --> Services
        WM -.eventa.-> SCS
        WS -.eventa.-> SCS
    end

    User[User OS] -.click tray.-> STI
    STI -.controls.-> Windows
```

## 11.12 Diagramme Entité-Relation simplifié (apps/server)

```mermaid
erDiagram
    USER ||--o{ CHARACTER : owns
    USER ||--o{ CHAT : has
    USER ||--o{ PROVIDER : configures
    USER ||--o{ FLUX_LEDGER : has
    USER ||--|| ACCOUNT : has
    CHAT ||--o{ MESSAGE : contains
    CHARACTER ||--o{ CHAT : appears_in
    PROVIDER ||--o{ CHAT : used_in

    USER {
        uuid id PK
        string email
        string name
        datetime createdAt
    }

    CHARACTER {
        uuid id PK
        uuid userId FK
        string name
        jsonb config
        datetime createdAt
    }

    CHAT {
        uuid id PK
        uuid userId FK
        uuid characterId FK
        uuid providerId FK
        string title
        datetime createdAt
    }

    MESSAGE {
        uuid id PK
        uuid chatId FK
        string role
        text content
        datetime createdAt
    }

    PROVIDER {
        uuid id PK
        uuid userId FK
        string kind
        string baseUrl
        string apiKey_encrypted
    }

    FLUX_LEDGER {
        uuid id PK
        uuid userId FK
        bigint delta
        string reason
        datetime createdAt
    }

    ACCOUNT {
        uuid id PK
        uuid userId FK
        string provider
        string providerAccountId
    }
```

## 11.13 Schéma : arbre d'héritage / dépendances des packages

```mermaid
graph BT
    plugin_protocol[plugin-protocol]
    server_shared[server-shared]
    server_shared --> plugin_protocol

    server_sdk[server-sdk]
    server_sdk --> server_shared
    server_runtime[server-runtime]
    server_runtime --> server_shared

    plugin_sdk[plugin-sdk]
    plugin_sdk --> plugin_protocol
    plugin_sdk --> server_sdk

    ui[ui]
    stage_shared[stage-shared]
    stage_ui[stage-ui]
    stage_ui --> ui
    stage_ui --> stage_shared
    stage_ui_three[stage-ui-three]
    stage_ui_three --> stage_ui
    stage_ui_live2d[stage-ui-live2d]
    stage_ui_live2d --> stage_ui
    stage_pages[stage-pages]
    stage_pages --> stage_ui
    stage_pages --> stage_ui_three
    stage_pages --> stage_ui_live2d
    stage_layouts[stage-layouts]
    stage_layouts --> stage_ui
    i18n[i18n]

    stage_web[apps/stage-web] --> stage_pages
    stage_web --> stage_layouts
    stage_web --> i18n
    stage_web --> server_sdk

    stage_tamagotchi[apps/stage-tamagotchi] --> stage_pages
    stage_tamagotchi --> stage_layouts
    stage_tamagotchi --> i18n
    stage_tamagotchi --> server_sdk
    stage_tamagotchi --> server_runtime

    stage_pocket[apps/stage-pocket] --> stage_pages
    stage_pocket --> stage_layouts
    stage_pocket --> i18n
    stage_pocket --> server_sdk

    discord_bot[services/discord-bot] --> server_sdk
    telegram_bot[services/telegram-bot] --> server_sdk
    minecraft_bot[services/minecraft] --> server_sdk
    satori_bot[services/satori-bot] --> server_sdk
    twitter_svc[services/twitter-services] --> server_sdk

    llm_orch[plugins/airi-plugin-llm-orchestrator] --> server_sdk
    llm_orch --> plugin_sdk
```

## 11.14 Schéma : flux de données audio-to-audio

```mermaid
flowchart LR
    Mic[🎤 Micro] --> AW[AudioWorklet 48kHz]
    AW --> VAD[VAD Worker Silero]
    VAD -->|speech segment| RESAMP[Resample 16kHz]
    RESAMP --> STT[STT Provider]
    STT --> IT[input:text:voice event]
    IT --> WS[WebSocket]
    WS --> SR[server-runtime]
    SR --> LO[llm-orchestrator]
    LO --> SX[xsai streamText]
    SX --> SRCHK[chunk stream]
    SRCHK --> WS2[WebSocket back]
    WS2 --> CL[server-sdk Client]
    CL --> TTS[Kokoro Worker]
    TTS --> PCM[Float32 PCM + visemes]
    PCM --> PB[Playback Manager]
    PB --> SPK[🔊 Speakers]
    PB --> LS[LipSync driver]
    LS --> VRM[VRM / Live2D mouth params]
    VRM --> CANVAS[🖼 Canvas]
```

---

> **Note** : tous les diagrammes ci-dessus sont au format Mermaid. Ils peuvent être rendus en SVG/PNG via des outils comme [mermaid.live](https://mermaid.live) ou directement par GitHub, VS Code, et la plupart des générateurs de documentation.
