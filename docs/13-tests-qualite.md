# 13 — Tests et qualité

## 13.1 Framework : Vitest 4.1

Tous les tests du monorepo AIRI sont écrits avec **Vitest**, la réponse moderne à Jest intégrée avec Vite.

### 13.1.1 Config racine `vitest.config.ts`

```typescript
import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    projects: [
      'apps/server',
      'apps/ui-server-auth',
      'apps/stage-tamagotchi',
      // + quelques packages sélectionnés
    ],
  },
})
```

> Attention : **tous les workspaces n'ont pas de tests**. La racine enregistre explicitement les projets qui en ont. Lancer `pnpm test:run` ne va donc pas couvrir *tout* le code.

### 13.1.2 Configs per-project

Chaque app/package ayant des tests possède son propre `vitest.config.ts` (ou `vitest.config.base.ts`). Exemple pour `stage-tamagotchi` :

```typescript
import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    include: [
      'src/**/*.test.ts',
      'scripts/**/*.test.ts',
    ],
    exclude: ['node_modules', '.git'],
  },
})
```

### 13.1.3 Commandes

```bash
# Tous les tests
pnpm test:run

# Tests d'un workspace
pnpm -F @proj-airi/stage-tamagotchi exec vitest run

# Un fichier précis
pnpm exec vitest run apps/stage-tamagotchi/src/renderer/stores/tools/builtin/widgets.test.ts

# Avec couverture
pnpm test   # = vitest --coverage
```

## 13.2 Stratégie de test du projet

Extrait de `AGENTS.md` :

> - **Vitest per project ; keep runs targeted for speed.**
> - **For any investigated bug or issue, try to reproduce it first with a test-only reproduction before changing production code.** Prefer a unit test; if that is not possible, use the smallest higher-level automated test.
> - **When an issue reproduction test is possible, include the tracker identifier in the test case name** (ex: `Issue #1234`).
> - **Add the actual report link as a comment directly above the regression test** (GitHub issue URL, Discord message URL, Linear issue URL).
> - **Mock IPC/services with `vi.fn`/`vi.mock`;** do not rely on real Electron runtime.
> - **For external providers/services, add both mock-based tests and integration-style tests** (with env guards) when feasible.
> - **Grow component/e2e coverage progressively** (Vitest browser env where possible).

## 13.3 Couverture actuelle

### 13.3.1 stage-tamagotchi

14 fichiers de test environ, couvrant :

**Main process**
- `src/main/services/airi/channel-server.test.ts` — config serveur
- `src/main/services/airi/plugins.test.ts` — plugin host
- `src/main/services/electron/auto-updater.test.ts` — auto-updater
- `src/main/app/lifecycle.test.ts` — lifecycle hooks

**Renderer**
- `src/renderer/stores/stage-window-lifecycle.test.ts`
- `src/renderer/stores/settings/server-channel.test.ts`
- `src/renderer/stores/stage-three-runtime-diagnostics.test.ts`
- `src/renderer/stores/tools/builtin/weather.test.ts`
- `src/renderer/stores/tools/builtin/widgets.test.ts`

**Scripts**
- `scripts/generate-update-manifest.test.ts`

### 13.3.2 services/minecraft

Tests sur les composants critiques :
- Cognitive engine
- Plugin system
- Isolated-vm sandbox

### 13.3.3 apps/server

Tests d'intégration des routes et services.

### 13.3.4 Tests Android (stage-pocket)

Dans `android/app/src/androidTest/` et `android/app/src/test/` :

- `MainActivityBridgeTest.kt` — test de communication JS ↔ Kotlin
- `HostWebSocketBridgeTest.kt` — test du bridge WebSocket

Ces tests JUnit sont lancés via Gradle :

```bash
cd apps/stage-pocket/android
./gradlew test
./gradlew connectedAndroidTest   # sur device/émulateur
```

## 13.4 Exemples de tests

### 13.4.1 Test unitaire : store Pinia avec mock

```typescript
// apps/stage-tamagotchi/src/renderer/stores/tools/builtin/weather.test.ts
import { setActivePinia, createPinia } from 'pinia'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { useWeatherToolStore } from './weather'

vi.mock('@vueuse/core', () => ({
  useFetch: vi.fn().mockResolvedValue({
    data: ref({ temperature: 20, condition: 'sunny' }),
  }),
}))

describe('Weather Tool Store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('fetches weather data for given city', async () => {
    const store = useWeatherToolStore()
    const result = await store.fetchWeather('Paris')
    expect(result.temperature).toBe(20)
    expect(result.condition).toBe('sunny')
  })

  it('handles fetch errors gracefully', async () => {
    // ...
  })
})
```

### 13.4.2 Test unitaire : helper de parsing LLM markers

```typescript
// packages/stage-ui/src/composables/llm-marker-parser.test.ts
import { describe, expect, it } from 'vitest'
import { parseLLMMarkers } from './llm-marker-parser'

describe('LLM Marker Parser', () => {
  it('extracts emotion markers', () => {
    const text = 'Hello <emotion:joy> world!'
    const result = parseLLMMarkers(text)
    expect(result.text).toBe('Hello  world!')
    expect(result.markers).toContainEqual({ kind: 'emotion', value: 'joy', offset: 6 })
  })

  it('extracts action markers', () => {
    const text = '<action:wave> Hi there'
    const result = parseLLMMarkers(text)
    expect(result.markers).toContainEqual({ kind: 'action', value: 'wave', offset: 0 })
  })

  it('handles multiple markers in one message', () => {
    // ...
  })

  // Ref: https://github.com/moeru-ai/airi/issues/842
  it('Issue #842: does not break on unclosed markers', () => {
    const text = 'Hello <emotion:joy world'
    const result = parseLLMMarkers(text)
    expect(result.text).toBe(text)  // pas de parsing si malformé
  })
})
```

### 13.4.3 Test d'intégration : server-runtime

```typescript
// packages/server-runtime/src/index.test.ts
import { describe, expect, it, beforeAll, afterAll } from 'vitest'
import { createServer } from './server'
import { Client } from '@proj-airi/server-sdk'

describe('server-runtime integration', () => {
  let server: Awaited<ReturnType<typeof createServer>>

  beforeAll(async () => {
    server = await createServer({ port: 0 })  // port 0 = random
  })

  afterAll(async () => {
    await server.close()
  })

  it('accepts connections and performs announce', async () => {
    const client = new Client({
      name: 'test-module',
      url: `ws://localhost:${server.port}/ws`,
      autoConnect: false,
    })

    const readyPromise = new Promise<void>((resolve) => {
      client.onConnectionStateChange((ctx) => {
        if (ctx.status === 'ready') resolve()
      })
    })

    await client.connect()
    await readyPromise
    expect(client.isReady).toBe(true)

    client.close()
  })

  it('rejects invalid tokens with a terminal error', async () => {
    // ...
  })

  it('broadcasts input:text to all consumers', async () => {
    // ...
  })
})
```

### 13.4.4 Test native Android (Kotlin)

```kotlin
// apps/stage-pocket/android/app/src/test/java/ai/moeru/airi_pocket/websocket/HostWebSocketBridgeTest.kt
package ai.moeru.airi_pocket.websocket

import org.junit.Test
import org.junit.Assert.*
import org.mockito.Mockito.*

class HostWebSocketBridgeTest {
  @Test
  fun `connect opens a websocket session`() {
    val mockFactory = mock(OkHttpHostWebSocketSessionFactory::class.java)
    val mockWebView = mock(WebView::class.java)
    val bridge = HostWebSocketBridge(mockWebView, mockFactory)

    bridge.connect("instance-1", "ws://localhost:6121/ws")

    verify(mockFactory).createSession(eq("instance-1"), eq("ws://localhost:6121/ws"), any())
  }

  @Test
  fun `send forwards message to the correct session`() {
    // ...
  }

  @Test
  fun `close tears down the session`() {
    // ...
  }
}
```

## 13.5 Mocking

### 13.5.1 `vi.mock` pour les modules externes

```typescript
vi.mock('electron', () => ({
  app: {
    whenReady: vi.fn().mockResolvedValue(undefined),
    on: vi.fn(),
    quit: vi.fn(),
  },
  BrowserWindow: vi.fn(),
  ipcMain: { on: vi.fn(), handle: vi.fn() },
}))
```

### 13.5.2 `@pinia/testing`

```typescript
import { createTestingPinia } from '@pinia/testing'

const pinia = createTestingPinia({
  createSpy: vi.fn,
  initialState: {
    provider: { apiKey: 'mock-key' },
  },
})
```

## 13.6 Test visuel : vishot

Le monorepo contient un framework de **visual regression testing** custom (`packages/vishot-*`). Il capture des screenshots de scènes (Live2D, VRM, UI pages) et les compare avec des baselines.

### 13.6.1 Commande

```bash
pnpm capture:tamagotchi
```

Exécute le scenario `demo-controls-settings-chat-websocket.ts` dans Electron et enregistre les captures dans `packages/scenarios-stage-tamagotchi-browser/artifacts/raw/`.

### 13.6.2 Scenario exemple

```typescript
// packages/scenarios-stage-tamagotchi-electron/src/scenarios/demo-controls-settings-chat-websocket.ts
import { defineScenario } from '@proj-airi/vishot-runtime'

export default defineScenario({
  name: 'demo-controls-settings-chat-websocket',
  async run({ page, capture }) {
    await page.goto('/settings')
    await capture('01-settings-open')

    await page.click('[data-testid="open-chat"]')
    await capture('02-chat-open')

    await page.type('[data-testid="chat-input"]', 'Hello')
    await page.keyboard.press('Enter')
    await page.waitForSelector('[data-testid="chat-response"]')
    await capture('03-chat-response')
  },
})
```

## 13.7 Linting et formatage

### 13.7.1 ESLint stack

- `@antfu/eslint-config` (base)
- `@moeru/eslint-config` (surcouche Moeru AI)
- `@electron-toolkit/eslint-config-ts` (règles Electron)
- `@unocss/eslint-plugin` (vérifie les classes UnoCSS)
- `eslint-plugin-oxlint` (intégration oxlint pour perf)

### 13.7.2 Règles notables

Extrait de la config (ce qui est enforcé) :

- **Pas de `console.log`** en production
- **Pas de `any` implicite**
- **Import order** strict (groupé)
- **Pas de `class` sauf extension Browser API** (convention AGENTS.md)
- **Style Vue** : kebab-case pour les noms de fichiers, `<script setup lang="ts">`
- **UnoCSS** : préférer tableaux `:class="[...]"` pour les listes de classes longues
- **Pas de backward-compat hacks** (pas de `_var` pour marquer inutilisé, etc.)

### 13.7.3 cspell

Orthographe des commentaires et docs. Config `cspell.config.yaml` à la racine avec les mots du domaine (AIRI, VRM, Live2D, etc.) ignorés.

### 13.7.4 knip (code mort)

```bash
pnpm knip
```

Détecte :
- Fichiers non importés
- Exports non utilisés
- Deps non utilisées
- Binaires manquants

## 13.8 TypeScript strict

Tous les `tsconfig.json` héritent (directement ou indirectement) de `tsconfig.json` racine :

```json
{
  "compilerOptions": {
    "target": "ESNext",
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "noUncheckedIndexedAccess": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "resolveJsonModule": true,
    "allowSyntheticDefaultImports": true,
    "jsx": "preserve",
    "jsxImportSource": "vue"
  }
}
```

Commande :
```bash
pnpm typecheck   # parallel sur tous les workspaces
```

## 13.9 Couverture de tests actuelle et goals

- **Server-runtime / SDK** : haut niveau de couverture (cœur critique)
- **stage-tamagotchi main** : tests lifecycle et services
- **stage-ui** : couverture partielle sur les composables les plus utilisés
- **apps/server** : tests routes + services
- **Services (bots)** : coverage variable, minecraft le plus testé
- **UI components** : couverture à développer progressivement

## 13.10 Règle post-tâche

Comme rappelé dans AGENTS.md :

> **Always run `pnpm typecheck && pnpm lint:fix` after finishing a task.**

Ces deux commandes sont prérequises pour qu'une PR soit mergeable.
