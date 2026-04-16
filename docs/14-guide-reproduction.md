# 14 — Guide de reproduction

Ce document décrit pas-à-pas comment **reproduire le projet AIRI depuis zéro**. À la fin, vous aurez un monorepo fonctionnel avec les trois apps principales et le server-runtime.

## 14.1 Prérequis système

### 14.1.1 OS supporté

- Windows 10/11 (avec WSL2 ou bash équivalent)
- macOS 12+ (Intel ou Apple Silicon)
- Linux Ubuntu 22.04+ / Fedora / Arch

### 14.1.2 Outils de base

| Outil | Version min | Commande d'installation |
|-------|-------------|------------------------|
| Node.js | 22.x LTS | `mise install nodejs@22` |
| pnpm | 10.32.1 | `corepack enable && corepack prepare pnpm@10.32.1 --activate` |
| Git | 2.40+ | `brew install git` ou équivalent |

Pour **Electron natif** :
- **Windows** : Visual Studio Build Tools, Python 3.11
- **macOS** : Xcode + command line tools
- **Linux** : `apt install build-essential libx11-dev libxkbfile-dev libsecret-1-dev libnss3`

Pour **iOS** :
- macOS + Xcode 15+ + CocoaPods (`sudo gem install cocoapods`)

Pour **Android** :
- Android Studio + Android SDK 34+ + JDK 17

## 14.2 Arborescence initiale à créer

```bash
mkdir airi && cd airi
git init
```

Créer la structure de base :

```bash
mkdir -p apps packages plugins services docs bucket patches nix
touch LICENSE README.md AGENTS.md CLAUDE.md
touch package.json pnpm-workspace.yaml tsconfig.json turbo.json vitest.config.ts
touch .gitignore .editorconfig .dockerignore
touch eslint.config.js uno.config.ts
```

## 14.3 Fichiers racine à produire

### 14.3.1 `package.json` racine

```json
{
  "name": "@proj-airi/root",
  "type": "module",
  "version": "0.1.0",
  "private": true,
  "packageManager": "pnpm@10.32.1",
  "description": "LLM powered virtual character",
  "license": "MIT",
  "scripts": {
    "postinstall": "pnpm exec simple-git-hooks && pnpm run build:packages",
    "dev": "pnpm -r -F @proj-airi/stage-web dev",
    "dev:tamagotchi": "pnpm -rF @proj-airi/stage-tamagotchi run dev",
    "dev:web": "pnpm -rF @proj-airi/stage-web run dev",
    "dev:pocket:ios": "pnpm -rF @proj-airi/stage-pocket run dev:ios",
    "dev:pocket:android": "pnpm -rF @proj-airi/stage-pocket run dev:android",
    "dev:server": "pnpm -rF @proj-airi/server-runtime run dev",
    "dev:ui": "pnpm -rF @proj-airi/stage-ui run story:dev",
    "build": "turbo run build -F=\"./packages/*\" -F=\"./apps/*\"",
    "build:packages": "turbo run build -F=\"./packages/*\"",
    "build:apps": "turbo run build -F=\"./apps/*\"",
    "build:web": "turbo run build -F @proj-airi/stage-web",
    "test:run": "vitest run",
    "lint": "moeru-lint .",
    "lint:fix": "moeru-lint --fix .",
    "typecheck": "pnpm -rF=\"./packages/*\" -F=\"./apps/*\" --parallel typecheck"
  },
  "devDependencies": {
    "@antfu/eslint-config": "^7.7.3",
    "@moeru/eslint-config": "0.1.0-beta.15",
    "@types/node": "^24.12.0",
    "eslint": "^10.1.0",
    "tsdown": "^0.21.4",
    "tsx": "^4.21.0",
    "turbo": "^2.8.20",
    "typescript": "^5.9.3",
    "vite": "^8.0.2",
    "vitest": "^4.1.1",
    "vue-tsc": "^3.0.7",
    "simple-git-hooks": "^2.13.1",
    "nano-staged": "catalog:"
  },
  "simple-git-hooks": {
    "pre-commit": "pnpm nano-staged"
  },
  "nano-staged": {
    "*": "moeru-lint --fix"
  }
}
```

### 14.3.2 `pnpm-workspace.yaml`

```yaml
catalogMode: prefer
shellEmulator: true
packages:
  - packages/**
  - plugins/**
  - integrations/**
  - services/**
  - examples/**
  - docs/**
  - apps/**
  - '!**/dist/**'
catalog:
  vue: ^3.5.30
  '@vueuse/core': 14.1.0
  pinia: ^3.0.4
  valibot: 1.2.0
  # ... (voir le pnpm-workspace.yaml complet du projet original)
```

### 14.3.3 `turbo.json`

```json
{
  "$schema": "https://turborepo.com/schema.json",
  "tasks": {
    "build": {
      "outputs": ["dist/**"]
    }
  }
}
```

### 14.3.4 `tsconfig.json` racine

```json
{
  "compilerOptions": {
    "target": "ESNext",
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "resolveJsonModule": true,
    "jsx": "preserve",
    "jsxImportSource": "vue"
  }
}
```

### 14.3.5 `vitest.config.ts` racine

```typescript
import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    projects: [
      'apps/stage-tamagotchi',
      'apps/server',
      'apps/ui-server-auth',
    ],
  },
})
```

### 14.3.6 `eslint.config.js` racine

```javascript
import moeru from '@moeru/eslint-config'

export default moeru({
  typescript: true,
  vue: true,
})
```

### 14.3.7 `uno.config.ts` racine

```typescript
import { defineConfig, presetIcons, presetUno, transformerDirectives } from 'unocss'
import presetWebFonts from '@unocss/preset-web-fonts'

export default defineConfig({
  presets: [
    presetUno(),
    presetIcons(),
    presetWebFonts({
      provider: 'google',
      fonts: {
        sans: 'Nunito',
        mono: 'Fira Code',
      },
    }),
  ],
  transformers: [transformerDirectives()],
})
```

## 14.4 Ordre de création des packages

Il faut créer les packages dans un ordre qui respecte leurs dépendances. Voici un ordre valide :

### Étape 1 — Types et contrats (aucune dépendance interne)
1. `packages/plugin-protocol` — types d'événements (`src/types/events.ts`)
2. `packages/server-shared` — validation + errors (`src/protocol/validate-event.ts`)

### Étape 2 — Serveur core
3. `packages/server-sdk` — client SDK (dépend de `server-shared`)
4. `packages/server-runtime` — runtime (dépend de `server-shared`)
5. `packages/plugin-sdk` — SDK plugin (dépend de `plugin-protocol`, `server-sdk`)

### Étape 3 — UI primitives
6. `packages/ui` — primitives reka-ui
7. `packages/i18n` — traductions
8. `packages/stage-shared` — helpers partagés

### Étape 4 — UI métier
9. `packages/stage-ui` — composants métier
10. `packages/stage-ui-three` — VRM + Three.js
11. `packages/stage-ui-live2d` — Live2D
12. `packages/stage-layouts` — layouts
13. `packages/stage-pages` — pages partagées

### Étape 5 — Infrastructure
14. `packages/audio`, `packages/pipelines-audio`, `packages/stream-kit`
15. `packages/electron-eventa`, `packages/electron-vueuse`, `packages/electron-screen-capture`
16. `packages/vite-plugin-warpdrive`

### Étape 6 — Apps
17. `apps/stage-web`
18. `apps/stage-tamagotchi` (embarque server-runtime)
19. `apps/stage-pocket`
20. `apps/server` (optionnel : si déploiement multi-user)

### Étape 7 — Services
21. `services/discord-bot`, `services/telegram-bot`, `services/minecraft`, `services/satori-bot`, `services/twitter-services`

### Étape 8 — Plugins
22. `plugins/airi-plugin-llm-orchestrator` (essentiel si pas de LLM côté client)
23. Autres plugins (claude-code, web-extension, bilibili-laplace, homeassistant)

## 14.5 Blueprint d'un package typique (`packages/server-sdk`)

### 14.5.1 package.json

```json
{
  "name": "@proj-airi/server-sdk",
  "type": "module",
  "version": "0.1.0",
  "main": "./dist/index.mjs",
  "module": "./dist/index.mjs",
  "types": "./dist/index.d.mts",
  "exports": {
    ".": {
      "types": "./dist/index.d.mts",
      "import": "./dist/index.mjs"
    },
    "./utils/node": {
      "types": "./dist/utils/node/index.d.mts",
      "import": "./dist/utils/node/index.mjs"
    }
  },
  "scripts": {
    "build": "tsdown",
    "dev": "tsdown --watch",
    "typecheck": "tsc --noEmit"
  },
  "dependencies": {
    "@proj-airi/server-shared": "workspace:*",
    "superjson": "catalog:"
  },
  "devDependencies": {
    "tsdown": "catalog:",
    "typescript": "catalog:"
  }
}
```

### 14.5.2 `tsdown.config.ts`

```typescript
import { defineConfig } from 'tsdown'

export default defineConfig({
  entry: [
    'src/index.ts',
    'src/utils/node/index.ts',
  ],
  format: ['esm'],
  dts: true,
  clean: true,
  sourcemap: true,
  unused: true,
})
```

### 14.5.3 `tsconfig.json`

```json
{
  "extends": "../../tsconfig.json",
  "compilerOptions": {
    "outDir": "dist",
    "rootDir": "src"
  },
  "include": ["src/**/*"]
}
```

### 14.5.4 `src/index.ts` (squelette du Client)

Reproduire la structure détaillée dans [06-packages-serveur.md § 6.4](06-packages-serveur.md).

## 14.6 Blueprint d'une app Vue (`apps/stage-web`)

### 14.6.1 package.json

```json
{
  "name": "@proj-airi/stage-web",
  "type": "module",
  "version": "0.1.0",
  "private": true,
  "scripts": {
    "dev": "vite",
    "build": "vite build",
    "preview": "vite preview",
    "typecheck": "vue-tsc --noEmit"
  },
  "dependencies": {
    "@proj-airi/i18n": "workspace:*",
    "@proj-airi/server-sdk": "workspace:*",
    "@proj-airi/stage-ui": "workspace:*",
    "@proj-airi/stage-pages": "workspace:*",
    "@proj-airi/stage-layouts": "workspace:*",
    "pinia": "catalog:",
    "vue": "catalog:",
    "vue-router": "^5.0.4"
  },
  "devDependencies": {
    "@vitejs/plugin-vue": "catalog:",
    "unplugin-vue-router": "^0.12.0",
    "vite": "catalog:",
    "vite-plugin-vue-layouts": "^0.12.0",
    "vue-tsc": "catalog:",
    "@intlify/unplugin-vue-i18n": "^6.0.0",
    "unocss": "catalog:"
  }
}
```

### 14.6.2 vite.config.ts minimal

```typescript
import { defineConfig } from 'vite'
import Vue from '@vitejs/plugin-vue'
import Layouts from 'vite-plugin-vue-layouts'
import VueRouter from 'unplugin-vue-router/vite'
import UnoCSS from 'unocss/vite'
import VueI18n from '@intlify/unplugin-vue-i18n/vite'
import path from 'node:path'

export default defineConfig({
  plugins: [
    VueRouter({
      extensions: ['.vue'],
      routesFolder: [
        { src: 'src/pages' },
        { src: '../../packages/stage-pages/src/pages' },
      ],
    }),
    Vue(),
    Layouts(),
    UnoCSS(),
    VueI18n({ include: path.resolve(__dirname, '../../packages/i18n/locales') }),
  ],
  resolve: {
    alias: {
      '@proj-airi/server-sdk': path.resolve(__dirname, '../../packages/server-sdk/src'),
      '@proj-airi/i18n':       path.resolve(__dirname, '../../packages/i18n/src'),
      '@proj-airi/stage-ui':   path.resolve(__dirname, '../../packages/stage-ui/src'),
    },
  },
})
```

### 14.6.3 `src/main.ts`

```typescript
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { setupLayouts } from 'virtual:generated-layouts'
import { routes } from 'vue-router/auto-routes'
import { createRouter, createWebHistory } from 'vue-router'
import { createI18n } from 'vue-i18n'
import messages from '@proj-airi/i18n/locales'
import App from './App.vue'

import 'uno.css'

const router = createRouter({
  history: createWebHistory(),
  routes: setupLayouts(routes),
})

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.use(createI18n({ legacy: false, locale: 'en', messages }))
app.mount('#app')
```

## 14.7 Blueprint de l'app Electron (`apps/stage-tamagotchi`)

### 14.7.1 Structure

```
apps/stage-tamagotchi/
├── src/
│   ├── main/
│   │   ├── index.ts        # composition injeca
│   │   ├── services/
│   │   ├── windows/
│   │   └── libs/
│   ├── preload/
│   │   ├── index.ts
│   │   └── shared.ts
│   ├── renderer/
│   │   ├── main.ts
│   │   ├── App.vue
│   │   ├── pages/
│   │   ├── stores/
│   │   └── components/
│   └── shared/
│       └── eventa.ts       # contrats IPC
├── resources/
│   └── icon.png
├── electron.vite.config.ts
├── electron-builder.config.ts
├── package.json
├── tsconfig.json
├── tsconfig.node.json
└── tsconfig.web.json
```

### 14.7.2 package.json

```json
{
  "name": "@proj-airi/stage-tamagotchi",
  "type": "module",
  "version": "0.1.0",
  "private": true,
  "main": "./out/main/index.js",
  "scripts": {
    "dev": "electron-vite dev",
    "build": "electron-vite build",
    "typecheck:node": "tsc --noEmit -p tsconfig.node.json --composite false",
    "typecheck:web": "vue-tsc --noEmit -p tsconfig.web.json --composite false",
    "typecheck": "pnpm run typecheck:node && pnpm run typecheck:web",
    "app:build": "pnpm run typecheck && pnpm run build && electron-builder"
  },
  "dependencies": {
    "@proj-airi/server-runtime": "workspace:*",
    "@proj-airi/server-sdk": "workspace:*",
    "@proj-airi/i18n": "workspace:*",
    "@proj-airi/stage-ui": "workspace:*",
    "@moeru/eventa": "catalog:",
    "injeca": "catalog:",
    "@guiiai/logg": "catalog:"
  },
  "devDependencies": {
    "electron": "catalog:",
    "electron-vite": "^5.0.0",
    "electron-builder": "^26.8.1",
    "electron-updater": "^6.8.3",
    "@electron-toolkit/utils": "^5.0.0",
    "@electron-toolkit/preload": "catalog:",
    "vue-tsc": "catalog:",
    "typescript": "catalog:"
  }
}
```

### 14.7.3 electron.vite.config.ts

```typescript
import { defineConfig, externalizeDepsPlugin } from 'electron-vite'
import Vue from '@vitejs/plugin-vue'
import VueRouter from 'unplugin-vue-router/vite'
import Layouts from 'vite-plugin-vue-layouts'
import UnoCSS from 'unocss/vite'
import path from 'node:path'

export default defineConfig({
  main: {
    plugins: [externalizeDepsPlugin()],
    build: {
      lib: { entry: 'src/main/index.ts' },
      rollupOptions: {
        output: {
          manualChunks: {
            debug: ['debug'],
            h3: ['h3', 'crossws'],
          },
        },
      },
    },
  },
  preload: {
    plugins: [externalizeDepsPlugin()],
    build: {
      lib: {
        entry: {
          index: 'src/preload/index.ts',
          'beat-sync': 'src/preload/beat-sync.ts',
        },
      },
    },
  },
  renderer: {
    plugins: [
      Vue(),
      VueRouter({ routesFolder: [{ src: 'src/renderer/pages' }] }),
      Layouts(),
      UnoCSS(),
    ],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, 'src/renderer'),
      },
    },
    build: {
      rollupOptions: {
        input: {
          main: path.resolve(__dirname, 'src/renderer/index.html'),
          'beat-sync': path.resolve(__dirname, 'src/renderer/beat-sync.html'),
        },
      },
    },
  },
})
```

### 14.7.4 electron-builder.config.ts

```typescript
import type { Configuration } from 'electron-builder'

const config: Configuration = {
  appId: 'ai.moeru.airi',
  productName: 'AIRI',
  directories: {
    output: 'release',
    buildResources: 'build',
  },
  files: [
    'out/**',
    'resources/**',
    'package.json',
    '!**/node_modules/*/{CHANGELOG.md,README.md}',
  ],
  mac: {
    category: 'public.app-category.utilities',
    hardenedRuntime: true,
    gatekeeperAssess: false,
    entitlements: 'build/entitlements.mac.plist',
  },
  win: {
    target: ['nsis'],
  },
  linux: {
    target: ['deb', 'rpm'],
    category: 'AudioVideo',
  },
  publish: {
    provider: 'github',
    owner: 'moeru-ai',
    repo: 'airi',
  },
}

export default config
```

## 14.8 Étapes d'installation et premier lancement

```bash
# Cloner / créer les fichiers ci-dessus
cd airi

# Installer toutes les dépendances et builder les packages
pnpm i

# (Optionnel) lancer tous les typechecks
pnpm typecheck

# Lancer le desktop en dev
pnpm dev:tamagotchi
```

Si tout est correctement configuré, une fenêtre Electron devrait s'ouvrir avec l'interface AIRI et se connecter au server-runtime embarqué.

## 14.9 Tests

```bash
pnpm test:run          # tous les tests
pnpm -F @proj-airi/stage-tamagotchi exec vitest run    # workspace spécifique
```

## 14.10 Build binaires

```bash
# Windows
pnpm -F @proj-airi/stage-tamagotchi app:build:win

# macOS (doit être lancé sur un Mac)
pnpm -F @proj-airi/stage-tamagotchi app:build:mac

# Linux
pnpm -F @proj-airi/stage-tamagotchi app:build:linux
```

Les binaires sortent dans `apps/stage-tamagotchi/release/`.

## 14.11 Configuration utilisateur (runtime)

Pour utiliser AIRI, l'utilisateur doit configurer :

1. **Un fournisseur LLM** (dans les settings → Providers)
   - OpenAI-compatible (OpenAI, OpenRouter, Ollama local, etc.)
   - API key + base URL + model name
2. **Un fournisseur STT** (Speech-to-Text) — optionnel si pas de voix
3. **Un fournisseur TTS** (Text-to-Speech) — optionnel si pas de voix
4. **Un caractère** (personnalité + prompt système + modèle VRM/Live2D)

Toutes ces options sont persistées dans `app.config.json` du dossier utilisateur Electron (`%APPDATA%/AIRI` sur Windows, `~/Library/Application Support/AIRI` sur macOS, `~/.config/AIRI` sur Linux).

## 14.12 Checklist finale de reproduction

- [ ] `pnpm i` passe sans erreur
- [ ] `pnpm typecheck` passe sans erreur
- [ ] `pnpm lint` passe sans erreur
- [ ] `pnpm test:run` passe (au moins pour les packages sans deps externes)
- [ ] `pnpm dev:web` lance stage-web et affiche la page d'accueil
- [ ] `pnpm dev:tamagotchi` lance stage-tamagotchi et affiche une fenêtre
- [ ] `pnpm dev:server` lance un server-runtime standalone sur port 6121
- [ ] Un client Node peut se connecter via `new Client({ name: 'test' })` et recevoir `module:announced`
- [ ] `pnpm build:packages` réussit
- [ ] `pnpm app:build:win/mac/linux` produit un binaire installable (selon l'OS)

Si tous les items sont cochés, la reproduction est complète.
