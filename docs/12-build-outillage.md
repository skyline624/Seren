# 12 — Build, outillage et CI

## 12.1 Outils de build utilisés

### 12.1.1 `pnpm` 10.32.1 + workspaces

Gestionnaire de paquets. Utilise :
- **Workspaces** via `pnpm-workspace.yaml` (packages, apps, services, plugins, docs, examples)
- **Catalog** (v10+) — centralise les versions de deps partagées
- **Overrides** — force certaines versions (nolyfills)
- **Patches** — fichiers de patch dans `patches/` appliqués au install
- **Built dependencies control** — `onlyBuiltDependencies` liste les seules deps autorisées à exécuter des scripts de post-install (sécurité)

### 12.1.2 `turbo` 2.8.20

Orchestrateur de tâches de build. Config : `turbo.json`.

**Utilisation** :
```bash
pnpm build           # turbo run build -F "./packages/*" -F "./apps/*"
pnpm build:packages  # turbo run build -F "./packages/*"
pnpm build:apps      # turbo run build -F "./apps/*"
pnpm build:web       # turbo run build -F @proj-airi/stage-web
```

Turbo profite du cache de fichiers (hash des inputs) pour éviter de rebuilder ce qui est déjà à jour. Les outputs sont par défaut stockés dans `dist/`.

### 12.1.3 `tsdown` 0.21.4

Bundler TypeScript → ESM/CJS, basé sur **oxc** (parser rust) et **rolldown** (bundler rust). C'est le **bundler par défaut pour les packages librairies** d'AIRI (pas pour les apps qui utilisent Vite).

**Config type** (`tsdown.config.ts`) :

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
})
```

**Avantages par rapport à `tsup`** :
- Utilise oxc-parser (plus rapide que swc/esbuild)
- Utilise rolldown (code splitting, tree shaking de niveau rollup)
- Sortie .mjs + .d.mts

### 12.1.4 `vite` 8.0.2

Bundler des apps web (`stage-web`, `stage-pocket`, `apps/server`, `apps/ui-server-auth`).

Plugins importants :
- `@vitejs/plugin-vue` — compilation SFC Vue
- `unplugin-vue-router` — auto-routes depuis `src/pages/`
- `vite-plugin-vue-layouts` — layouts pattern (déprécié, sera remplacé)
- `unocss/vite` — génération CSS à la volée
- `@intlify/unplugin-vue-i18n` — bundle des locales
- `vite-plugin-pwa` — service worker PWA
- `vite-plugin-mkcert` — HTTPS local en dev
- `vite-plugin-inspect` — debug des transformations
- `@proj-airi/unplugin-fetch` — téléchargement d'assets externes au build
- `@proj-airi/vite-plugin-warpdrive` — upload assets lourds vers S3

### 12.1.5 `electron-vite` 5

Bundler spécifique Electron qui gère les trois process (main / preload / renderer). Config : `electron.vite.config.ts` qui définit trois sections :

```typescript
import { defineConfig, externalizeDepsPlugin } from 'electron-vite'

export default defineConfig({
  main: {
    plugins: [externalizeDepsPlugin()],
    build: { /* ... */ },
  },
  preload: {
    plugins: [externalizeDepsPlugin()],
    build: { lib: { /* multi-entry */ } },
  },
  renderer: {
    plugins: [ /* Vue, UnoCSS, router, layouts, i18n, ... */ ],
    build: { /* HTML entrypoints */ },
  },
})
```

### 12.1.6 `electron-builder` 26.8.1

Outil de packaging et distribution Electron. Config : `electron-builder.config.ts`.

**Plates-formes supportées** :

**Windows**
- NSIS installer
- Per-arch update channels : `latest-x64.yml`, `latest-arm64.yml`
- Desktop shortcut option

**macOS**
- Format `.dmg` + `.zip`
- Support `.icon` (Xcode 26+) ou `.icns`
- Hardened runtime + notarization (requiert cert Apple Developer + `APPLE_ID`, `APPLE_APP_SPECIFIC_PASSWORD`, `APPLE_TEAM_ID`)
- Entitlements : `com.apple.security.device.audio-input`, `device.camera`
- Per-arch channels : `latest-x64-mac.yml`, `latest-arm64-mac.yml`

**Linux**
- Targets : deb, rpm
- Per-arch channels
- Desktop category : `AudioVideo`

**Files inclus** :
- `out/**` (build electron-vite)
- `resources/**`
- `package.json`

**Files exclus** :
- `electron` (paquet npm, embarqué binairement)
- Fichiers sources TS
- Test dirs

**Exceptions spéciales** : on garde `debug` et `superjson` dans le bundle car nécessaires au runtime.

### 12.1.7 `electron-updater` 6.8.3

Bibliothèque d'auto-update intégrée. Fonctionne avec les manifests générés par electron-builder (YAML par arch et par channel).

**Channels** : `stable`, `alpha`, `beta`, `nightly`, `canary` (configurables par l'utilisateur via settings)

### 12.1.8 `vue-tsc` (typecheck)

Pour les apps Vue, `vue-tsc` est utilisé à la place de `tsc` car il comprend les `.vue` files. La commande `typecheck` dans les apps fait souvent une double passe :

```json
"typecheck:node": "tsc --noEmit -p tsconfig.node.json --composite false",
"typecheck:web": "vue-tsc --noEmit -p tsconfig.web.json --composite false",
"typecheck": "pnpm run typecheck:node && pnpm run typecheck:web"
```

### 12.1.9 `moeru-lint` (ESLint wrapper)

Outil de lint propriétaire Moeru AI qui wrap ESLint + oxlint avec une config personnalisée. La config racine (`eslint.config.js`) utilise :

- `@moeru/eslint-config`
- `@antfu/eslint-config`
- `@electron-toolkit/eslint-config-ts`
- `@unocss/eslint-plugin`
- `eslint-plugin-oxlint`

Commandes :
```bash
pnpm lint          # moeru-lint .
pnpm lint:fix      # moeru-lint --fix .
```

### 12.1.10 `knip` 6.0.4

Outil de détection du code mort (exports inutilisés, deps non utilisées). Config : `knip.json` (minimal, 135B).

```bash
pnpm knip
```

### 12.1.11 `cspell` + `crowdin`

- `cspell.config.yaml` — règles d'orthographe pour la documentation
- `crowdin.yml` — synchronisation des traductions

### 12.1.12 Nix flake (optionnel)

`flake.nix` + `default.nix` fournissent un environnement de développement reproductible via Nix. Pas obligatoire mais supporté pour les contributeurs Linux avec Nix installé.

## 12.2 Pipeline complet de build

```
┌────────────────────────────────────────────────────┐
│ pnpm install                                       │
│  1. Résout les catalogs                            │
│  2. Applique les overrides                         │
│  3. Applique les patches (`patches/*.patch`)       │
│  4. Déploie node_modules virtuel                    │
│  5. Hook postinstall :                              │
│      a. simple-git-hooks (pre-commit)              │
│      b. pnpm run build:packages                     │
└──────────────────┬─────────────────────────────────┘
                   │
                   ▼
┌────────────────────────────────────────────────────┐
│ build:packages (turbo run build -F ./packages/*)   │
│                                                    │
│  Pour chaque package qui a un script "build" :     │
│    → tsdown (la majorité)                          │
│    → vite build (stage-ui pour histoire)           │
│    → vue-tsc (typecheck préalable)                 │
└──────────────────┬─────────────────────────────────┘
                   │
                   ▼
┌────────────────────────────────────────────────────┐
│ build apps (turbo run build -F ./apps/*)           │
│                                                    │
│  Chaque app appelle son propre build :             │
│    → stage-web       : vite build                  │
│    → stage-pocket    : vite build + cap sync       │
│    → stage-tamagotchi: electron-vite build         │
│                       + typecheck (tsc + vue-tsc)  │
│    → server          : tsdown                      │
│    → ui-server-auth  : vite build                  │
└──────────────────┬─────────────────────────────────┘
                   │
                   ▼
┌────────────────────────────────────────────────────┐
│ Packaging (selon la cible)                         │
│                                                    │
│  → stage-tamagotchi : electron-builder             │
│    - app:build:win   → .exe (NSIS)                  │
│    - app:build:mac   → .dmg + .zip                  │
│    - app:build:linux → .deb + .rpm                  │
│                                                    │
│  → stage-web        : statique dans dist/          │
│  → stage-pocket     : cap sync → Xcode / Gradle    │
└────────────────────────────────────────────────────┘
```

## 12.3 Cycle de développement local

### 12.3.1 Bootstrap initial

```bash
git clone https://github.com/moeru-ai/airi
cd airi
pnpm i          # Installe + applique patches + build packages
```

### 12.3.2 Lancer stage-web en dev

```bash
pnpm dev            # = pnpm -r -F @proj-airi/stage-web dev
# Ou :
pnpm dev:web
```

Ouvre `http://localhost:5173` (port par défaut Vite).

### 12.3.3 Lancer stage-tamagotchi en dev

```bash
pnpm dev:tamagotchi
```

Électron s'ouvre avec hot-reload activé.

### 12.3.4 Lancer server-runtime en dev

```bash
pnpm dev:server
```

Démarre le runtime sur `ws://localhost:6121/ws`.

### 12.3.5 Lancer stage-pocket en dev

```bash
# iOS
pnpm dev:pocket:ios
# Android
pnpm dev:pocket:android
```

Requiert Xcode (iOS) ou Android Studio (Android). Le code Vue est servi depuis Vite et les apps natives pointent vers `CAPACITOR_DEV_SERVER_URL`.

### 12.3.6 Histoire storybook

```bash
pnpm dev:ui
```

Ouvre Histoire sur le port configuré pour explorer tous les composants de `stage-ui`.

## 12.4 Scripts de release

### 12.4.1 Bump version

Via `bumpp` (config dans `bump.config.ts`) :

```bash
pnpm dlx bumpp
```

Incrémente la version dans le root `package.json` et tous les workspaces synchronisés.

### 12.4.2 Build binaires desktop

```bash
pnpm build:packages                # pré-requis
pnpm -F @proj-airi/stage-tamagotchi app:build:win
pnpm -F @proj-airi/stage-tamagotchi app:build:mac
pnpm -F @proj-airi/stage-tamagotchi app:build:linux
```

Les artefacts sortent dans `apps/stage-tamagotchi/release/` (.exe, .dmg, .deb, .rpm + .yml manifest).

## 12.5 CI / GitHub Actions

Le dossier `.github/workflows/` contient les pipelines. Les principaux jobs attendus :

1. **Lint** : `pnpm lint`
2. **Typecheck** : `pnpm typecheck`
3. **Test** : `pnpm test:run`
4. **Build** : `pnpm build` (cache Turbo activé)
5. **Release** :
   - Trigger sur tag `v*`
   - Build des 3 plateformes desktop (3 runners)
   - Upload vers GitHub Releases avec les manifests auto-updater

Variables d'environnement CI attendues :
- `GITHUB_TOKEN` (release)
- `APPLE_ID`, `APPLE_APP_SPECIFIC_PASSWORD`, `APPLE_TEAM_ID` (notarization mac)
- `CSC_LINK`, `CSC_KEY_PASSWORD` (signing windows)
- `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` (WarpDrive CDN pour build web)
- `POSTHOG_KEY` (analytics)

## 12.6 Git hooks

Via `simple-git-hooks` :

```json
"simple-git-hooks": {
  "pre-commit": "pnpm nano-staged"
},
"nano-staged": {
  "*": "moeru-lint --fix"
}
```

À chaque `git commit`, `nano-staged` exécute `moeru-lint --fix` sur chaque fichier staged. Si un fichier ne passe pas, le commit est bloqué.

## 12.7 Conventions Git

- Branches : `username/feat/short-name`, `username/fix/short-name`, `username/refactor/short-name`
- Commits : **Conventional Commits** (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`)
- Gitmoji optionnel
- PRs : rebase pull, summary + test plan

## 12.8 `.dockerignore`

Le projet inclut un `.dockerignore` minimal, suggérant qu'un build Docker existe pour `apps/server` ou d'autres services. Il exclut `node_modules`, `dist`, `.git`, `.github`, `patches`, et les artefacts electron-builder.

## 12.9 Éditeurs supportés

Le repo contient des configs spécifiques pour plusieurs IDE/agents IA :

- `.cursor/` — Cursor IDE rules
- `.zed/` — Zed IDE settings
- `.gemini/` — Google Gemini (Code Assist) rules
- `.agents/` — Fichiers de contexte Moeru AI
- `AGENTS.md` + `CLAUDE.md` — instructions projet pour les agents IA (Claude Code, Codex)

Ces fichiers ne sont pas des builds mais ils influencent le workflow de développement.

## 12.10 Environnements requis (`.tool-versions`)

```
nodejs 22.x
pnpm 10.32.1
```

Le fichier `.tool-versions` est compatible **asdf** et **mise** (gestionnaires de versions multi-langages). Les outils comme `mise install` dans ce dossier installent automatiquement les bonnes versions.

**Pour Electron et native deps** :
- **macOS** : Xcode + command line tools
- **Windows** : Visual Studio Build Tools ou Python + node-gyp
- **Linux** : `libx11-dev`, `libxkbfile-dev`, `libsecret-1-dev`, etc.

**Pour les plateformes mobiles** :
- **iOS** : macOS + Xcode 15+ + CocoaPods
- **Android** : Android Studio + Android SDK 34+ + JDK 17

**Pour les tests visuels (vishot)** :
- Playwright Chromium : `pnpm exec playwright install chromium`
