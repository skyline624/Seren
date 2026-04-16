# 09 — Plugins AIRI

Les plugins vivent dans `plugins/` et sont des **modules de plus haut niveau** que les services : ils utilisent `@proj-airi/plugin-sdk` en plus de `@proj-airi/server-sdk`, et bénéficient du cycle de vie complet (capability offer, permissions, reload à chaud).

## 9.1 Différence « service » vs « plugin »

| Aspect | Service | Plugin |
|--------|---------|--------|
| Localisation | `services/` | `plugins/` |
| Cycle de vie | Process standalone lancé manuellement | Géré par plugin-host (dans stage-tamagotchi main) ou autonome |
| Hot-reload | Non | Oui (via plugin-host) |
| Capabilities | Non | Oui |
| Permissions | Non | Oui |
| Public-cible | Intégration plateforme tierce | Extension verticale AIRI |

---

## 9.2 `airi-plugin-llm-orchestrator`

### 9.2.1 Rôle

**Plugin critique** : moteur d'orchestration LLM côté serveur qui gère :
- Provider management (OpenAI, Anthropic, OpenRouter, Ollama, local…)
- Streaming de réponses avec back-pressure
- Persistence des sessions de chat (PostgreSQL via Drizzle)
- Multi-fournisseur dynamique par session
- Support des tools / function calling

Ce plugin est la pièce qui remplace/complète le module `consciousness` (client-side) pour les déploiements server-side.

### 9.2.2 Arborescence

```
plugins/airi-plugin-llm-orchestrator/
├── src/
│   ├── index.ts            # Entry (exporte un plugin sdk compatible)
│   ├── run.ts              # Main CLI runner
│   ├── session/
│   │   └── store.ts        # SessionStore (in-memory + DB)
│   ├── providers/
│   │   └── manager.ts      # ProviderManager (multi-LLM)
│   ├── streaming/
│   │   └── bridge.ts       # StreamBridge (token chunks → events)
│   └── db/
│       └── schema.ts       # Drizzle schemas (sessions, messages, providers)
├── dist/
├── package.json
└── bin/run.mjs
```

### 9.2.3 Configuration CLI

Lancé en standalone via `cac` :

```bash
node dist/run.mjs \
  --server-url ws://localhost:6121/ws \
  --database-url postgres://user:pwd@localhost/airi \
  --llm-base-url https://api.openai.com/v1 \
  --llm-api-key sk-xxx \
  --llm-model gpt-4o
```

Ou en tant que plugin embarqué dans `stage-tamagotchi` (le plugin-host le charge).

### 9.2.4 Connection AIRI

```typescript
import { Client } from '@proj-airi/server-sdk'

const client = new Client({
  name: 'proj-airi:plugin-llm-orchestrator',
  url: serverUrl,
  possibleEvents: [
    'ui:configure',
    'input:text',
    'input:text:voice',
    'output:gen-ai:chat:message',
  ],
})
```

### 9.2.5 Consumer groups

Le plugin s'enregistre comme consumer pour certains events avec une **delivery config** :

```typescript
client.send({
  type: 'module:consumer:register',
  data: {
    event: 'input:text',
    group: 'llm-orchestrator',
    priority: 10,
  },
})
```

Cela permet d'avoir plusieurs instances (load-balancées) sur les plateformes où le throughput est critique.

Autre consumer group utilisé : `chat-ingestion` (pour le stockage en base).

### 9.2.6 Streaming via `@xsai/stream-text`

```typescript
import { streamText } from '@xsai/stream-text'
import { createOpenAI } from '@xsai-ext/providers'

async function handleInput(event: InputTextEvent) {
  const provider = providerManager.get(session.providerId)
  const stream = await streamText({
    model: provider(session.model),
    messages: session.messages,
    tools: session.tools,
  })

  for await (const chunk of stream.textStream) {
    client.send({
      type: 'output:gen-ai:chat:message:chunk',
      data: { sessionId: session.id, chunk },
    })
  }
}
```

### 9.2.7 Dépendances

- `@xsai/stream-text`
- `@xsai-ext/providers` (OpenAI, Anthropic, OpenRouter, Ollama builders)
- `drizzle-orm` + `postgres`
- `cac` (CLI)
- `@proj-airi/server-sdk`
- `@proj-airi/plugin-sdk`

---

## 9.3 `airi-plugin-claude-code`

### 9.3.1 Rôle

Intègre **Claude Code** (le CLI Anthropic) comme plugin AIRI. Permet de streamer les hook events de Claude Code (par exemple à chaque tool call) vers un AIRI runtime pour qu'un agent AIRI puisse observer, guider ou interagir avec une session Claude Code.

### 9.3.2 Fonctionnement

- Le plugin expose une CLI `airi-claude-code send` qui lit des événements depuis **stdin**
- À chaque hook Claude Code (PreToolUse, PostToolUse, Stop), on appelle `airi-claude-code send --hook-type=<kind>` en pipant le payload JSON
- Le script forwarde l'event à AIRI via `Client.send()`

### 9.3.3 Configuration Claude Code

Dans `.claude/settings.json` de l'utilisateur :

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "*",
        "hooks": [
          {
            "type": "command",
            "command": "airi-claude-code send --hook-type=PreToolUse"
          }
        ]
      }
    ]
  }
}
```

### 9.3.4 Fichier clé : `src/run.ts`

```typescript
import { cac } from 'cac'
import { Client } from '@proj-airi/server-sdk'

const cli = cac('airi-claude-code')

cli.command('send', 'Send a hook event')
  .option('--hook-type <type>', 'Hook type')
  .action(async (options) => {
    const payload = await readStdinJson()
    const client = new Client({
      name: 'claude-code',
      url: process.env.AIRI_URL,
    })
    await client.ready()
    client.send({
      type: 'plugin:claude-code:hook',
      data: { hookType: options.hookType, payload },
    })
    client.close()
  })

cli.parse()
```

### 9.3.5 Dépendances

- `@anthropic-ai/claude-code` (dev)
- `@proj-airi/server-sdk`
- `cac`
- `vue` (pour la structure du manifest plugin)

### 9.3.6 Bin

```json
{
  "bin": {
    "airi-claude-code": "./dist/run.js"
  }
}
```

---

## 9.4 `airi-plugin-homeassistant`

### 9.4.1 Rôle

Plugin prévu pour contrôler un **Home Assistant** : allumer/éteindre lumières, lire capteurs, lancer scripts, écouter events. Permet à AIRI de faire des commandes vocales domotiques.

### 9.4.2 Statut

**Work in progress** — stub seulement. Le squelette est en place (`src/index.ts`), la logique reste à implémenter avec la bibliothèque `home-assistant-js-websocket` ou équivalent.

### 9.4.3 Plan

```typescript
// src/index.ts (future)
import { definePlugin } from '@proj-airi/plugin-sdk'
import { createConnection } from 'home-assistant-js-websocket'

export default definePlugin({
  id: 'homeassistant',
  version: '0.1.0',
  configSchema: {
    id: 'homeassistant.config',
    version: 1,
    schema: {
      type: 'object',
      required: ['url', 'accessToken'],
      properties: {
        url: { type: 'string' },
        accessToken: { type: 'string' },
      },
    },
  },

  async setup({ client, config }) {
    const ha = await createConnection({
      createSocket: () => new WebSocket(`${config.url}/api/websocket`),
      auth: { accessToken: config.accessToken },
    })

    client.onEvent('input:text', async (event) => {
      const intent = parseIntent(event.data.text)
      if (intent.type === 'turnOn') {
        await ha.callService('light', 'turn_on', { entity_id: intent.entity })
      }
    })
  },
})
```

---

## 9.5 `airi-plugin-bilibili-laplace`

### 9.5.1 Rôle

Bridge entre le **live chat Bilibili** (via LAPLACE Event Bridge) et AIRI. Permet à AIRI de voir et réagir aux messages du tchat pendant un livestream Bilibili.

### 9.5.2 Statut

**WIP** — stub, le skeleton avec `Client` et `@laplace.live/event-bridge-sdk` est en place.

### 9.5.3 Plan

```typescript
import { Client } from '@proj-airi/server-sdk'
import { createEventBridge } from '@laplace.live/event-bridge-sdk'

const airi = new Client({ name: 'bilibili-laplace' })
const bridge = createEventBridge({ roomId: process.env.BILIBILI_ROOM_ID })

bridge.onDanmaku((msg) => {
  airi.send({
    type: 'input:text',
    data: {
      text: msg.content,
      overrides: {
        sessionId: `bilibili-${msg.uid}`,
        messagePrefix: `[${msg.username}] `,
      },
    },
  })
})
```

### 9.5.4 Dépendances

- `@laplace.live/event-bridge-sdk`
- `@laplace.live/event-types`
- `@proj-airi/server-sdk`

---

## 9.6 `airi-plugin-web-extension`

### 9.6.1 Rôle

**Extension navigateur** (Chrome + Firefox) bâtie avec **WXT** (le framework Vue/Vite pour web extensions). Permet à AIRI de :
- Voir les onglets ouverts
- Injecter un chat overlay dans les pages
- Extraire le contenu visible
- Contrôler le navigateur (navigation, clics)

### 9.6.2 Technologies

- **WXT** (framework web extension moderne)
- **Vue 3** (UI de l'extension : popup, options, content script)
- **UnoCSS**
- **@vueuse/core**
- **@moeru/eventa** (IPC entre background / content / popup)
- **@proj-airi/ui** (primitives)
- **@proj-airi/server-sdk** (connexion AIRI)

### 9.6.3 Builds

Le package produit plusieurs variantes :
- Chrome dev build
- Chrome production build
- Firefox dev build
- Firefox production build

Scripts :

```json
{
  "scripts": {
    "dev": "wxt",
    "dev:firefox": "wxt -b firefox",
    "build": "wxt build",
    "build:firefox": "wxt build -b firefox",
    "zip": "wxt zip",
    "zip:firefox": "wxt zip -b firefox"
  }
}
```

### 9.6.4 Architecture

```
Web page          Popup Vue (extension icon)
   ▲                      ▲
   │content script        │
   ▼                      ▼
      Background script
            │
            │ @proj-airi/server-sdk → WebSocket
            ▼
       server-runtime
```

Le background script tient une instance `Client` AIRI ouverte en permanence et relaye les events entre le serveur et les content scripts.

---

## 9.7 Résumé des plugins

| Plugin | Statut | Rôle |
|--------|--------|------|
| airi-plugin-llm-orchestrator | ✅ stable | Orchestrateur LLM serveur (essentiel) |
| airi-plugin-claude-code | ✅ stable | Hooks Claude Code → AIRI |
| airi-plugin-homeassistant | 🚧 WIP | Domotique |
| airi-plugin-bilibili-laplace | 🚧 WIP | Live chat Bilibili |
| airi-plugin-web-extension | ✅ stable | Extension navigateur |

## 9.8 Écrire un nouveau plugin

Blueprint minimal avec `@proj-airi/plugin-sdk` :

```typescript
// plugins/my-plugin/src/index.ts
import { definePlugin } from '@proj-airi/plugin-sdk'

export default definePlugin({
  id: 'my-plugin',
  version: '0.1.0',
  dependencies: [
    { role: 'llm:orchestrator', optional: false },
  ],
  permissions: {
    apis: [
      { key: 'output:gen-ai:chat:message', actions: ['emit'], required: true },
    ],
  },
  configSchema: {
    id: 'my-plugin.config',
    version: 1,
    schema: {
      type: 'object',
      required: ['apiKey'],
      properties: {
        apiKey: { type: 'string', description: 'API key for my service' },
      },
    },
  },

  async setup({ client, config, log }) {
    log.info('Plugin starting')

    // Offrir une capability
    client.send({
      type: 'module:contribute:capability:offer',
      data: {
        identity: client.identity,
        capability: {
          id: 'my-plugin.hello',
          name: 'Hello World',
          description: 'Says hello to the user',
        },
      },
    })

    // Écouter des events
    const unsubscribe = client.onEvent('input:text', async (event) => {
      if (event.data.text.includes('/hello')) {
        client.send({
          type: 'output:gen-ai:chat:message',
          data: { message: { role: 'assistant', content: 'Hello from my-plugin!' } },
        })
      }
    })

    return { unsubscribe }
  },

  async teardown({ state }) {
    state.unsubscribe?.()
  },
})
```

Puis ajouter une entrée dans `pnpm-workspace.yaml` (déjà couvert par `plugins/**`), un `package.json` :

```json
{
  "name": "@proj-airi/my-plugin",
  "version": "0.1.0",
  "type": "module",
  "main": "./dist/index.mjs",
  "scripts": {
    "build": "tsdown",
    "dev": "tsdown --watch"
  },
  "dependencies": {
    "@proj-airi/plugin-sdk": "workspace:*",
    "@proj-airi/server-sdk": "workspace:*"
  }
}
```

Et un `tsdown.config.ts` :

```typescript
import { defineConfig } from 'tsdown'

export default defineConfig({
  entry: ['src/index.ts'],
  format: ['esm'],
  dts: true,
  clean: true,
})
```
