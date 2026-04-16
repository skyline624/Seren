# 08 — Services d'intégration

Les services vivent dans `services/` et sont tous des **modules clients** qui se connectent au server-runtime via `@proj-airi/server-sdk`. Chaque service est un projet Node autonome avec son propre `package.json`.

## 8.1 `@proj-airi/discord-bot`

### 8.1.1 Rôle

Connecte un serveur Discord à AIRI. Écoute les messages textes et vocaux dans les channels configurés, les forwarde à AIRI comme événements `input:text` ou `input:text:voice`, puis joue la réponse (texte + TTS) dans Discord.

### 8.1.2 Arborescence

```
services/discord-bot/
├── src/
│   ├── index.ts                    # Bootstrap
│   ├── adapters/
│   │   └── airi-adapter.ts         # Pont AIRI ↔ Discord
│   ├── commands/                   # Slash commands Discord
│   ├── handlers/                   # Event handlers (messageCreate, etc.)
│   ├── voice/                      # Voice manager (join/leave/play)
│   └── config.ts
├── package.json
└── .env                            # Clés API (discord token, openai key, etc.)
```

### 8.1.3 Intégration AIRI

Fichier `src/adapters/airi-adapter.ts` (ligne ~78) :

```typescript
import { Client } from '@proj-airi/server-sdk'

export function createAiriAdapter(opts: AdapterOptions) {
  const client = new Client({
    name: 'discord',
    url: opts.airiUrl,
    token: opts.airiToken,
    possibleEvents: [
      'input:text',
      'input:text:voice',
      'input:voice',
      'module:configure',
      'output:gen-ai:chat:message',
    ],
  })

  client.onEvent('output:gen-ai:chat:message', (event) => {
    // Route la réponse vers le bon channel Discord
    const source = event.data.input?.data
    if (source && 'discord' in source) {
      const { guildId, channelId } = source.discord
      routeToDiscord(guildId, channelId, event.data)
    }
  })

  return client
}
```

### 8.1.4 Message flow

```
Discord user → messageCreate event (discord.js)
  → adapter.emit({ type: 'input:text', data: { text, discord: {...} } })
  → server-runtime broadcast
  → llm-orchestrator consumes
  → output:gen-ai:chat:message
  → adapter receives, routes to discord.js channel.send()
  + if voice channel connected: generate speech with @xsai/generate-speech → pipeline → @discordjs/voice
```

### 8.1.5 Dépendances

- `discord.js` (14+)
- `@discordjs/voice`
- `@proj-airi/server-sdk`
- `@xsai/generate-speech`
- `@huggingface/transformers` (pour du processing léger si nécessaire)

### 8.1.6 Scripts

```json
{
  "scripts": {
    "start": "tsx --env-file .env src/index.ts"
  }
}
```

---

## 8.2 `@proj-airi/minecraft-bot`

### 8.2.1 Rôle

Fait jouer AIRI à Minecraft. Le bot utilise `mineflayer` (bot Minecraft Java) avec une palette de plugins (pathfinder, pvp, auto-eat, tool, collect-block, armor-manager) et un **moteur cognitif LLM** qui planifie et exécute des actions (*scripts isolés en VM*).

### 8.2.2 Arborescence

```
services/minecraft/
├── src/
│   ├── main.ts                     # Bootstrap avec DI injeca
│   ├── airi/
│   │   └── airi-bridge.ts          # Pont vers server-runtime
│   ├── agents/                     # Agents cognitifs (plan, act, observe)
│   ├── mineflayer/                 # Wrappers mineflayer
│   ├── plugins/                    # Plugins custom
│   ├── debugger/                   # Debugger REPL WebSocket
│   ├── tools/                      # Outils exposés au LLM
│   └── config/
├── tests/
└── package.json
```

### 8.2.3 Cognitive loop

Le bot reçoit des commandes AIRI via l'événement `spark:command` (intents `plan | action | pause | resume`). Pour chaque action :

1. L'agent LLM reçoit un contexte du monde (position, inventaire, entities, chunks)
2. Il génère un script JavaScript à exécuter
3. Le script tourne dans un **isolated-vm** (sandbox isolée) avec accès limité à l'API mineflayer
4. Les résultats (succès/échec, observations) sont renvoyés via `context:update`

### 8.2.4 Contrats AIRI

```typescript
client.onEvent('spark:command', async (event) => {
  const intent = event.data.intent
  switch (intent) {
    case 'plan':
      const plan = await cognitiveEngine.plan(event.data.goal)
      client.send({ type: 'context:update', data: { text: `Plan: ${plan}` } })
      break
    case 'action':
      const result = await executeAction(event.data.action)
      client.send({ type: 'context:update', data: { text: `Result: ${result}` } })
      break
    case 'pause':
      cognitiveEngine.pause()
      break
    case 'resume':
      cognitiveEngine.resume()
      break
  }
})
```

### 8.2.5 Dépendances remarquables

- `mineflayer` 4.33 (**patché**, voir `patches/mineflayer@4.33.0.patch`)
- `mineflayer-pathfinder` (**patché**)
- `mineflayer-armor-manager`, `mineflayer-auto-eat`, `mineflayer-collectblock`, `mineflayer-pvp`, `mineflayer-tool`
- `isolated-vm` (pour l'exécution sandboxée de scripts LLM)
- `prismarine-*` (Minecraft data libs)
- `canvas` (pour vision capture in-game)
- `@modelcontextprotocol/sdk` (pour exposer les tools en MCP)

### 8.2.6 Scripts

```json
{
  "scripts": {
    "dev": "tsx watch src/main.ts",
    "start": "node dist/main.js",
    "test": "vitest run"
  }
}
```

Les tests utilisent des snapshots avec `NODE_OPTIONS='--experimental-vm-modules'` pour les modules ESM en environnement sandbox.

---

## 8.3 `@proj-airi/telegram-bot`

### 8.3.1 Rôle

Bot Telegram qui permet à AIRI de discuter dans des chats Telegram. Utilise **grammY** (framework moderne remplaçant telegraf) et persiste les conversations dans PostgreSQL via Drizzle.

### 8.3.2 Arborescence

```
services/telegram-bot/
├── src/
│   ├── index.ts                    # Entry
│   ├── bot/                        # Handlers grammy
│   ├── db/
│   │   ├── schema.ts               # Drizzle schemas
│   │   └── client.ts
│   ├── handlers/
│   │   ├── message.ts              # Message handler avec embeddings
│   │   ├── file.ts                 # Gestion fichiers/média
│   │   └── audio.ts
│   ├── services/
│   │   ├── airi.ts                 # Bridge vers server-runtime
│   │   └── embedding.ts            # Embeddings pour la mémoire (xsai/embed)
│   ├── scripts/
│   │   └── embed-chat.ts           # Script pour ré-indexer un chat
│   └── telemetry/                  # OpenTelemetry setup
├── drizzle/                        # Migrations
└── drizzle.config.ts
```

### 8.3.3 OpenTelemetry

Le bot est instrumenté avec OpenTelemetry :
- **Traces** : exportées en OTLP
- **Metrics** : gauges de messages traités, erreurs
- **Logs** : via `@guiiai/logg` avec exporter OTLP

### 8.3.4 Embeddings pour la mémoire

Chaque message reçu est embedé (via `@xsai/embed` — modèle `text-embedding-3-small` ou équivalent) et stocké dans PostgreSQL avec pgvector. Cela permet de recherches sémantiques dans l'historique.

### 8.3.5 Scripts

```json
{
  "scripts": {
    "start": "tsx --env-file .env src/index.ts",
    "db:generate": "drizzle-kit generate",
    "db:push": "drizzle-kit push",
    "script:embed-chat": "tsx --env-file .env src/scripts/embed-chat.ts"
  }
}
```

### 8.3.6 Dépendances

- `grammy` (Telegram framework)
- `drizzle-orm` + `postgres`
- `@xsai/embed`
- `ffmpeg-static` + `fluent-ffmpeg` (traitement audio/vidéo)
- `@opentelemetry/*`

---

## 8.4 `@proj-airi/satori-bot`

### 8.4.1 Rôle

Adaptateur **Satori protocol** — un standard de messagerie unifié pour bots multi-plateformes (Koishi et écosystème chinois). Permet à AIRI de se brancher sur QQ, WeChat, Kook, Telegram, Discord via un seul adapter.

### 8.4.2 Particularités

- Utilise `@electric-sql/pglite` (PostgreSQL embarqué en WebAssembly) pour persister sans serveur externe
- Implémente un **event registry** qui mappe les actions Satori standards (`message.create`, `friend.add`, etc.) aux événements AIRI
- Boucle périodique pour le polling des channels (certains backends Satori n'offrent pas de webhooks)

### 8.4.3 Dépendances clés

- `@electric-sql/pglite`
- `drizzle-orm`
- `best-effort-json-parser` (parser résilient pour les payloads LLM mal formés)

---

## 8.5 `@proj-airi/twitter-services`

### 8.5.1 Rôle

Intégration Twitter/X via **automatisation Playwright** (pas d'API officielle payante). Expose deux adapters :
1. **AiriAdapter** — se connecte au server-runtime comme module `x`
2. **MCPAdapter** — expose les mêmes outils via le protocole **MCP** (Model Context Protocol) pour Claude Code / VS Code

### 8.5.2 Arborescence

```
services/twitter-services/
├── src/
│   ├── main.ts                     # Bootstrap dual adapters
│   ├── adapters/
│   │   ├── airi.ts                 # Adapter AIRI
│   │   └── mcp.ts                  # Adapter MCP
│   ├── browser/                    # Gestion Playwright (Chromium)
│   ├── operations/
│   │   ├── timeline.ts             # Lire la timeline
│   │   ├── user.ts                 # Profils utilisateur
│   │   └── tweet.ts                # Posting / lecture
│   └── auth/                       # Login Twitter
├── playwright.config.ts
└── package.json
```

### 8.5.3 Adapter AIRI

```typescript
import { Client } from '@proj-airi/server-sdk'

const client = new Client({
  name: 'x',
  possibleEvents: [
    'module:authenticate',
    'module:authenticated',
    'module:announce',
    'ui:configure',
    'input:text',
  ],
})

client.onEvent('input:text', async (event) => {
  const text = event.data.text
  // Parse command: "post a tweet: xxx", "read timeline", etc.
  const command = parseCommand(text)
  const result = await browserExecute(command)
  client.send({
    type: 'context:update',
    data: { text: `Twitter action result: ${result}` },
  })
})
```

### 8.5.4 Adapter MCP

Expose les opérations comme des **tools MCP** utilisables par un agent externe :

```json
{
  "tools": [
    {
      "name": "twitter_post",
      "description": "Post a tweet",
      "inputSchema": { /* ... */ }
    },
    {
      "name": "twitter_read_timeline",
      "description": "Read the home timeline",
      "inputSchema": { /* ... */ }
    }
  ]
}
```

Démarrage : `pnpm -F @proj-airi/twitter-services mcp:ui` lance l'inspecteur MCP sur localhost.

### 8.5.5 Dépendances

- `playwright` (Chromium headful ou headless)
- `@modelcontextprotocol/sdk`
- `h3` (pour l'inspecteur)
- `@proj-airi/server-sdk`

### 8.5.6 Scripts

```json
{
  "scripts": {
    "dev": "playwright install chromium && tsx watch src/main.ts",
    "mcp:ui": "tsx src/mcp-ui.ts"
  }
}
```

---

## 8.6 Conventions communes aux services

Tous les services partagent :

1. **Module name stable** : `discord`, `minecraft`, `telegram`, `satori`, `x`
2. **Énumération de `possibleEvents`** dans `new Client({ possibleEvents: [...] })`
3. **Réception de `module:configure`** pour accepter une config dynamique (tokens, URL API)
4. **Propagation de `source`** dans les événements `input:text` : le service injecte la provenance (guildId, chatId, channelId, etc.) pour que la réponse soit routée correctement
5. **Résilience** : utilisent l'auto-reconnect du `Client` AIRI par défaut. Pour les services eux-mêmes (discord.js, grammy, mineflayer), ils implémentent leurs propres retries.
6. **Logging** : `@guiiai/logg` avec format Pretty en dev et JSON en prod
7. **Configuration par `.env`** : token AIRI, token service, URL du runtime

## 8.7 Comment ajouter un nouveau service

Le blueprint d'un nouveau service (ex: un bot Matrix) serait :

```
services/matrix-bot/
├── package.json     # nom: @proj-airi/matrix-bot, déps: matrix-js-sdk, @proj-airi/server-sdk
├── tsconfig.json
├── src/
│   ├── index.ts     # bootstrap
│   ├── adapter.ts   # createAiriAdapter
│   └── matrix.ts    # wrapper matrix-js-sdk
└── .env.example
```

Code minimal :

```typescript
// src/index.ts
import { Client } from '@proj-airi/server-sdk'
import { startMatrixClient } from './matrix'

const airi = new Client({
  name: 'matrix',
  url: process.env.AIRI_URL ?? 'ws://localhost:6121/ws',
  token: process.env.AIRI_TOKEN,
  possibleEvents: ['input:text', 'output:gen-ai:chat:message'],
})

const matrix = await startMatrixClient()

matrix.on('message', (roomId, userId, text) => {
  airi.send({
    type: 'input:text',
    data: { text, overrides: { sessionId: roomId } },
  })
})

airi.onEvent('output:gen-ai:chat:message', async (event) => {
  const roomId = event.data.input?.data?.overrides?.sessionId
  if (roomId) {
    await matrix.sendMessage(roomId, event.data.message.content)
  }
})
```

Le service est alors prêt à être lancé via `pnpm -F @proj-airi/matrix-bot start`.
