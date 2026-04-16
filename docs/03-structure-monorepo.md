# 03 — Structure du monorepo

## 3.1 Racine du projet

```
airi/
├── .github/              # Workflows CI, issue templates
├── .agents/              # Fichiers de contexte pour Claude Code, Cursor, Gemini, Zed
├── .cursor/              # Règles Cursor IDE
├── .gemini/              # Règles Gemini
├── .zed/                 # Règles Zed IDE
├── apps/                 # 6 applications
├── bucket/               # Assets et binaires externes (ignorés par git)
├── docs/                 # Site de documentation (dont ce dossier analyse-exhaustive)
├── integrations/         # Intégrations tierces (placeholder)
├── nix/                  # Configuration Nix (flake.nix)
├── packages/             # 42 packages internes (lib, ui, server, outils)
├── patches/              # Patches pnpm appliqués à des deps externes
├── plugins/              # 5 plugins AIRI
├── scripts/              # Scripts de maintenance et build
├── services/             # 5 services d'intégration (bots, etc.)
├── AGENTS.md             # Guide pour les agents IA (conventions projet)
├── CLAUDE.md             # Renvoie vers AGENTS.md
├── LICENSE               # MIT
├── README.md             # README principal
├── bump.config.ts        # Config bumpp (release)
├── crowdin.yml           # Config traduction Crowdin
├── cspell.config.yaml    # Config orthographe cspell
├── default.nix / flake.nix # Environnement Nix
├── eslint.config.js      # Config ESLint (oxlint + @antfu/eslint-config)
├── knip.json             # Config knip (détection code mort)
├── package.json          # Racine pnpm
├── pnpm-lock.yaml        # Lockfile (1.2 MB)
├── pnpm-workspace.yaml   # Définition workspaces + catalog + overrides
├── posthog.config.ts     # Config PostHog (analytics)
├── rustfmt.toml          # Config formatage Rust (pour crates/)
├── skills-lock.json      # Lockfile pour skills (outillage Moeru AI)
├── sponsorkit.config.js  # Config sponsors
├── tsconfig.json         # Config TS racine
├── turbo.json            # Config Turbo (build orchestrator)
├── uno.config.ts         # Config UnoCSS (global)
├── vite-env.d.ts         # Types Vite globaux
└── vitest.config.ts      # Config Vitest racine
```

## 3.2 Gestion des workspaces (pnpm)

Le fichier `pnpm-workspace.yaml` définit :

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
```

### Catalog

pnpm 10 introduit les **catalogs** : une façon de centraliser les versions de dépendances partagées. AIRI utilise le *default catalog* pour 100+ packages (Vue, Vite, Three.js, xsai, etc.) plus un catalog `vitest:` spécifique.

Cela permet à tous les packages workspace d'écrire `"vue": "catalog:"` dans leur package.json au lieu de dupliquer un numéro de version.

### Overrides

Plusieurs dépendances externes sont remplacées par des équivalents "nolyfill" pour réduire la taille bundle :
- `array-flatten`, `is-core-module`, `isarray`, `safe-buffer`, `safer-buffer`, `side-channel`, `string.prototype.matchall` → `@nolyfill/*`
- `axios` → `feaxios` (implémentation légère)
- `onnxruntime-web` → ré-override à la dernière version

### Patches

Les patches locaux (`patches/`) sont essentiels :
- `@mediapipe/tasks-vision` — corrections de typings / API
- `@xsai/generate-text@0.5.0-beta.2`, `@xsai/shared-chat@0.5.0-beta.2`, `@xsai/stream-text@0.5.0-beta.2` — corrections upstream
- `crossws@0.4.4` — corrections pour l'intégration h3
- `mineflayer-pathfinder`, `mineflayer@4.33.0` — pour le bot Minecraft
- `pixi-live2d-display` — adaptation au Live2D Cubism 5
- `srvx` — adapter HTTP universel

## 3.3 Organisation de `apps/`

```
apps/
├── component-calling/    # Expérimental / démo
├── server/               # Serveur SaaS multi-user (Hono + Drizzle + Redis + better-auth + Stripe)
├── stage-pocket/         # Mobile (Capacitor iOS + Android)
├── stage-tamagotchi/     # Desktop (Electron)
├── stage-web/            # PWA web (Vue + Vite)
└── ui-server-auth/       # UI d'authentification pour apps/server
```

Chaque app a sa propre `package.json`, `tsconfig.json`, scripts de build, et (pour les apps Vue) `vite.config.ts`. Le nom dans package.json suit le pattern `@proj-airi/<name>`.

### Filtres pnpm usuels

```bash
pnpm -F @proj-airi/stage-web <script>
pnpm -F @proj-airi/stage-tamagotchi <script>
pnpm -F @proj-airi/stage-pocket <script>
pnpm -F @proj-airi/server <script>
```

## 3.4 Organisation de `packages/`

42 packages, regroupés logiquement ci-dessous :

### Noyau UI (7 packages)
- `stage-ui` — composants métier, stores, composables, workers (VAD, TTS)
- `stage-ui-three` — bindings Three.js + VRM
- `stage-ui-live2d` — wrapper pixi-live2d-display
- `stage-shared` — utilitaires partagés (auth, beat-sync, env-vars, qr-probe)
- `stage-pages` — templates de page partagés
- `stage-layouts` — layouts de stage
- `ui` — primitives reka-ui

### i18n + fontes (6 packages)
- `i18n` — traductions (9 langues, YAML)
- `font-chillroundm` — police customisée
- `font-cjkfonts-allseto` — fontes CJK
- `font-departure-mono` — police monospace
- `font-xiaolai` — police de fallback zh
- `unocss-preset-fonts` — preset UnoCSS pour les fontes

### Serveur (4 packages)
- `server-runtime` — runtime H3 + crossws
- `server-sdk` — client SDK
- `server-sdk-shared` — shared entre sdk et runtime
- `server-shared` — types + validation d'enveloppe
- `server-schema` — schémas JSON (cache désactivé dans turbo.json)

### Plugin protocol (2 packages)
- `plugin-protocol` — types d'événements WebSocket + metadata
- `plugin-sdk` — SDK pour écrire des plugins

### Audio & pipelines (4 packages)
- `audio` — utilitaires Web Audio
- `audio-pipelines-transcribe` — utilitaires de transcription
- `pipelines-audio` — speech pipeline, playback manager
- `stream-kit` — queue & stream utilities

### Drivers modèles (2 packages)
- `model-driver-lipsync` — wrapper wlipsync + Live2D
- `model-driver-mediapipe` — MediaPipe motion capture

### Caractères & mémoire (3 packages)
- `core-character` — pipeline character (stub)
- `memory-pgvector` — mémoire pgvector (WIP)
- `ccc` — contexte clustering (alias interne)

### Electron (3 packages)
- `electron-eventa` — contrats eventa pour Electron
- `electron-screen-capture` — capture d'écran desktopCapturer
- `electron-vueuse` — composables VueUse pour Electron

### DuckDB (2 packages)
- `drizzle-duckdb-wasm` — adaptateur Drizzle ORM
- `duckdb-wasm` — wrapper

### Scenarios / Vishot (3 packages)
- `scenarios-stage-tamagotchi-browser` — scénarios visuels navigateur
- `scenarios-stage-tamagotchi-electron` — scénarios visuels desktop
- `vishot-runner-browser` — runner visual testing browser
- `vishot-runner-electron` — runner visual testing electron
- `vishot-runtime` — runtime commun

### Outillage (4 packages)
- `cap-vite` — intégration Capacitor + Vite
- `vite-plugin-warpdrive` — upload assets S3
- `ui-loading-screens` — écrans de chargement
- `ui-transitions` — transitions UI

### Legacy
- `crates/` (Rust) — ancienne version Tauri (désormais remplacée par Electron)

## 3.5 Organisation de `services/`

```
services/
├── discord-bot/       # Bot Discord (discord.js + @discordjs/voice)
├── minecraft/         # Bot Minecraft (mineflayer + isolated-vm)
├── satori-bot/        # Adapter Satori protocol
├── telegram-bot/      # Bot Telegram (grammy + Drizzle)
└── twitter-services/  # MCP server + AIRI adapter (Playwright)
```

Chaque service est un package standalone (`@proj-airi/<name>`) qui importe `@proj-airi/server-sdk` et se connecte au runtime en tant que module.

## 3.6 Organisation de `plugins/`

```
plugins/
├── airi-plugin-bilibili-laplace/   # LAPLACE Event Bridge pour Bilibili Live
├── airi-plugin-claude-code/        # Intégration Claude Code (@anthropic-ai/claude-code)
├── airi-plugin-homeassistant/      # Home Assistant (WIP)
├── airi-plugin-llm-orchestrator/   # Orchestrateur LLM serveur (xsai + Drizzle)
└── airi-plugin-web-extension/      # Extension navigateur (WXT)
```

## 3.7 Configuration `turbo.json`

```json
{
  "$schema": "https://turborepo.com/schema.json",
  "tasks": {
    "build": {
      "outputs": ["dist/**"]
    },
    "@proj-airi/server-schema#build": {
      "cache": false
    },
    "@proj-airi/electron-vueuse#build": {
      "dependsOn": ["@proj-airi/electron-eventa#build"],
      "outputs": ["dist/**"]
    }
  }
}
```

- Toutes les tâches `build` sortent dans `dist/**`
- `server-schema` désactive le cache (sortie dépendante du temps)
- `electron-vueuse` dépend explicitement de `electron-eventa` (le graph implicite pnpm ne suffit pas)

## 3.8 Scripts racine principaux

Extrait de `package.json` racine :

```json
{
  "scripts": {
    "postinstall": "pnpm exec simple-git-hooks && pnpm run build:packages",
    "dev": "pnpm -r -F @proj-airi/stage-web dev",
    "dev:tamagotchi": "pnpm -rF @proj-airi/stage-tamagotchi run dev",
    "dev:web": "pnpm -rF @proj-airi/stage-web run dev",
    "dev:server": "pnpm -rF @proj-airi/server-runtime run dev",
    "dev:pocket:ios": "pnpm -rF @proj-airi/stage-pocket run dev:ios",
    "dev:pocket:android": "pnpm -rF @proj-airi/stage-pocket run dev:android",
    "dev:ui": "pnpm -rF @proj-airi/stage-ui run story:dev",
    "build": "turbo run build -F=\"./packages/*\" -F=\"./apps/*\"",
    "build:packages": "turbo run build -F=\"./packages/*\"",
    "build:apps": "turbo run build -F=\"./apps/*\"",
    "build:web": "turbo run build -F @proj-airi/stage-web",
    "build:tamagotchi": "pnpm -rF @proj-airi/stage-tamagotchi run app:build",
    "test": "vitest --coverage",
    "test:run": "vitest run",
    "lint": "moeru-lint .",
    "lint:fix": "moeru-lint --fix .",
    "typecheck": "pnpm -rF=\"./packages/*\" -F=\"./apps/*\" -F=\"./docs\" --parallel typecheck"
  }
}
```

Points importants :
- **`postinstall`** installe les git hooks *et* construit tous les packages (condition pour que les apps se lancent)
- **`dev`** lance par défaut `stage-web`
- **`build`** orchestre packages + apps via Turbo (cache + parallélisation)
- **`lint`** utilise `moeru-lint` (wrapper ESLint + oxlint spécifique Moeru AI)

## 3.9 Commandes pré-commit

`simple-git-hooks` configuré ainsi dans `package.json` :

```json
"simple-git-hooks": {
  "pre-commit": "pnpm nano-staged"
},
"nano-staged": {
  "*": "moeru-lint --fix"
}
```

À chaque commit, `nano-staged` exécute `moeru-lint --fix` sur chaque fichier staged. Les erreurs de lint bloquent le commit.

## 3.10 Règle : `pnpm typecheck && pnpm lint:fix` après toute tâche

Comme rappelé dans AGENTS.md et CLAUDE.md :

> **After finishing any task, always run:**
> ```bash
> pnpm typecheck && pnpm lint:fix
> ```

Cette règle est non-négociable : le pipeline CI l'exige.
