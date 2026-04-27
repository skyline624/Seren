# 16 — Annexes

## 16.1 Glossaire

| Terme | Définition |
|-------|-----------|
| **AIRI** | Projet open-source « cyber living being », inspiré par Neuro-sama |
| **Stage** | Surface frontale (UI) visible par l'utilisateur. Trois apps : stage-web, stage-tamagotchi, stage-pocket |
| **Server channel** | Runtime backend (server-runtime) exposé par WebSocket via server-sdk |
| **Module** | Un peer connecté au server-runtime. Peut être un frontend, un bot, un plugin, un service |
| **Plugin** | Un module avec cycle de vie complet (capability offer, permissions, hot-reload) utilisant `@proj-airi/plugin-sdk` |
| **Service** | Un module standalone Node dans `services/`, souvent un bot externe |
| **Eventa** | Bibliothèque `@moeru/eventa` pour IPC/RPC type-safe |
| **Injeca** | Container d'injection de dépendances (`injeca`) pour composer les services |
| **VRM** | Virtual Reality Model — format 3D pour avatars (spécification VRM Consortium) |
| **Live2D** | SDK 2D Cubism pour l'animation d'images par déformation de mesh |
| **VAD** | Voice Activity Detection (détection de parole) |
| **STT** | Speech-to-Text (transcription) |
| **TTS** | Text-to-Speech (synthèse vocale) |
| **MCP** | Model Context Protocol — standard Anthropic pour exposer des outils à un LLM |
| **WarpDrive** | Plugin Vite custom pour upload d'assets lourds vers S3 CDN |
| **xsai** | Bibliothèque Moeru AI (`@xsai/*`) équivalent léger de Vercel AI SDK |
| **crossws** | Adapter WebSocket universel (H3 / Nitro) |
| **Valibot** | Bibliothèque de validation type-safe (alternative à Zod, préférée dans AIRI) |
| **superjson** | Sérialiseur JSON avec support des types non-natifs (Date, Map, BigInt, etc.) |
| **tsdown** | Bundler TS/ESM basé sur oxc + rolldown |
| **reka-ui** | Primitives headless Vue (équivalent Radix UI) |
| **Capacitor** | Framework wrapper pour déployer une app web en natif mobile |
| **OPFS** | Origin Private File System — API navigateur pour stockage privé |
| **nanoid** | Générateur d'IDs uniques compacts |
| **Hono** | Framework HTTP ultralight (apps/server) |
| **h3** | Framework HTTP léger utilisé par le server-runtime |
| **Drizzle ORM** | ORM TypeScript sans magie, SQL first |
| **pglite** | PostgreSQL WASM embarqué |
| **pgvector** | Extension PostgreSQL pour la recherche vectorielle (embeddings) |
| **Mineflayer** | Bibliothèque Node.js pour créer des bots Minecraft Java |
| **LAPLACE Event Bridge** | Service de streaming d'événements chat Bilibili |
| **Satori Protocol** | Protocole unifié pour bots multi-plateformes |

## 16.2 Tableau des ports par défaut

| Service | Port | Fichier de config |
|---------|------|-------------------|
| server-runtime | 6121 | `AIRI_SERVER_PORT` |
| stage-web (dev) | 5173 | `vite.config.ts` |
| stage-pocket (dev) | 5273 | `apps/stage-pocket/vite.config.ts` |
| ui-server-auth (dev) | 5174 | `apps/ui-server-auth/vite.config.ts` |
| stage-ui histoire | 6006 | `histoire.config.ts` |

## 16.3 Variables d'environnement principales

### 16.3.1 server-runtime

```bash
AIRI_SERVER_PORT=6121
AIRI_SERVER_AUTH_TOKEN=<token secret>
AIRI_SERVER_CORS_ORIGINS=https://airi.moeru.ai,http://localhost:5173
AIRI_SERVER_RATE_LIMIT_MAX=100
AIRI_SERVER_RATE_LIMIT_WINDOW_MS=10000
AIRI_SERVER_READ_TIMEOUT_MS=30000
LOG_LEVEL=info
LOG_FORMAT=pretty
```

### 16.3.2 Client server-sdk (bots et plugins)

```bash
AIRI_URL=ws://localhost:6121/ws
AIRI_TOKEN=<token secret>
```

### 16.3.3 apps/server (SaaS)

```bash
DATABASE_URL=postgres://user:pwd@localhost:5432/airi
REDIS_URL=redis://localhost:6379
BETTER_AUTH_SECRET=<random secret>
BETTER_AUTH_URL=http://localhost:3000
STRIPE_SECRET_KEY=sk_test_xxx
STRIPE_WEBHOOK_SECRET=whsec_xxx
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
```

### 16.3.4 Bots

```bash
# Discord
DISCORD_TOKEN=<bot token>

# Telegram
TELEGRAM_TOKEN=<bot token>
DATABASE_URL=postgres://...

# Minecraft
MINECRAFT_HOST=localhost
MINECRAFT_PORT=25565
MINECRAFT_USERNAME=airi

# Twitter
TWITTER_USERNAME=<login>
TWITTER_PASSWORD=<password>
```

### 16.3.5 Build / CI

```bash
GITHUB_TOKEN=<token release>
APPLE_ID=<dev apple>
APPLE_APP_SPECIFIC_PASSWORD=<app-specific>
APPLE_TEAM_ID=<team id>
CSC_LINK=<windows code signing cert>
CSC_KEY_PASSWORD=<cert password>
AWS_ACCESS_KEY_ID=<warpdrive s3>
AWS_SECRET_ACCESS_KEY=<warpdrive s3>
POSTHOG_KEY=<analytics>
```

### 16.3.6 Capacitor pocket

```bash
CAPACITOR_DEV_SERVER_URL=http://192.168.1.100:5273
CAP_TARGET=android|ios
CAP_KEYSTORE_PATH=./keystore.jks
CAP_KEYSTORE_PASSWORD=<pwd>
CAP_KEYSTORE_ALIAS=airi
CAP_KEYSTORE_ALIAS_PASSWORD=<pwd>
VITE_CAP_SYNC_IOS_AFTER_BUILD=1
```

## 16.4 Liste complète des événements du protocole

### 16.4.1 Événements de contrôle transport

- `transport:connection:heartbeat`

### 16.4.2 Événements de registre

- `registry:modules:sync`
- `registry:modules:health:healthy`
- `registry:modules:health:unhealthy`

### 16.4.3 Événements d'authentification

- `module:authenticate`
- `module:authenticated`

### 16.4.4 Événements de compatibilité

- `module:compatibility:request`
- `module:compatibility:result`

### 16.4.5 Événements d'annonce

- `module:announce`
- `module:announced`
- `module:de-announced`

### 16.4.6 Événements de permissions

- `module:permissions:declare`
- `module:permissions:request`
- `module:permissions:granted`
- `module:permissions:denied`
- `module:permissions:current`

### 16.4.7 Événements de préparation

- `module:prepared`

### 16.4.8 Événements de configuration

- `module:configuration:needed`
- `module:configuration:validate:request`
- `module:configuration:validate:response`
- `module:configuration:validate:status`
- `module:configuration:plan:request`
- `module:configuration:plan:response`
- `module:configuration:plan:status`
- `module:configuration:commit`
- `module:configuration:commit:status`
- `module:configuration:configured`

### 16.4.9 Événements de capabilities

- `module:contribute:capability:offer`
- `module:contribute:capability:configuration:needed`
- `module:contribute:capability:configuration:*` (validate, plan, commit)
- `module:contribute:capability:activated`

### 16.4.10 Événements de statut et consumer

- `module:status`
- `module:consumer:register`
- `module:consumer:unregister`

### 16.4.11 Événements d'entrée

- `input:text`
- `input:text:voice`
- `input:voice`

### 16.4.12 Événements de sortie

- `output:gen-ai:chat:message`
- `output:gen-ai:chat:message:chunk`
- `output:gen-ai:chat:message:end`

### 16.4.13 Événements de contexte

- `context:update`

### 16.4.14 Événements UI

- `ui:configure`

### 16.4.15 Événements d'erreur

- `error`
- `error:permission`

### 16.4.16 Événements spécifiques services

- `spark:command` (Minecraft)
- `plugin:claude-code:hook` (Claude Code)

## 16.5 Références externes

### 16.5.1 Dépôts liés

- [moeru-ai/airi](https://github.com/moeru-ai/airi) — le monorepo principal
- [moeru-ai/eventa](https://github.com/moeru-ai/eventa) — @moeru/eventa
- [moeru-ai/xsai](https://github.com/moeru-ai/xsai) — xsai AI SDK
- [unjs/h3](https://github.com/unjs/h3) — HTTP framework
- [unjs/crossws](https://github.com/unjs/crossws) — adapter WebSocket
- [pixiv/three-vrm](https://github.com/pixiv/three-vrm) — VRM loader
- [pixijs/pixi-live2d-display](https://github.com/RaSan147/pixi-live2d-display) — Live2D for PIXI
- [ricky0123/vad](https://github.com/ricky0123/vad) — Silero VAD (browser)

### 16.5.2 Documentation

- [DeepWiki AIRI](https://deepwiki.com/moeru-ai/airi)
- [Site officiel](https://airi.moeru.ai)
- [Discord](https://discord.gg/TgQ3Cu2F7A)

### 16.5.3 Standards

- [VRM specification](https://vrm.dev/)
- [Live2D Cubism SDK](https://www.live2d.com/en/sdk/)
- [Model Context Protocol](https://spec.modelcontextprotocol.io/)
- [Satori protocol](https://satori.js.org/)

## 16.6 FAQ

### Q1 — Pourquoi Electron et pas Tauri ?

> Historiquement le projet était en Tauri (voir `crates/` Rust legacy), mais il a été migré vers Electron pour un meilleur support multi-fenêtres, transparence, click-through et intégration des WebWorkers audio. Tauri 2 n'était pas assez mature sur ces besoins au moment de la migration.

### Q2 — Pourquoi Valibot et pas Zod ?

> Valibot est modulaire et tree-shakable (5-10x plus léger que Zod). AIRI en a besoin pour le bundle web. Les schemas se composent fonctionnellement au lieu d'utiliser une API de classes.

### Q3 — Pourquoi xsai et pas Vercel AI SDK ?

> xsai (`@xsai/*`) est développé par Moeru AI et vise le même objectif que Vercel AI SDK mais avec une empreinte plus faible, sans dépendance runtime aux frameworks (pas de Next.js-only), et avec un modèle de providers standardisé qui s'intègre naturellement dans les stores Pinia.

### Q4 — Pourquoi Pinia et pas Zustand ou Redux ?

> Pinia est l'état recommandé pour Vue 3, avec TypeScript first-class et une API réactive basée sur Composition API. Zustand/Redux seraient gauches en Vue.

### Q5 — Comment ajouter un nouveau client mobile (ex: Windows Phone 🙃) ?

> 1. Créer une app Capacitor (ou équivalent) dans `apps/stage-windowsphone/`
> 2. Réutiliser le code Vue commun via les alias vers `packages/stage-pages`, `packages/stage-ui`, etc.
> 3. Implémenter un `websocket-bridge` natif si l'API WebSocket du WebView n'est pas suffisante
> 4. Passer le `websocketConstructor` au `Client` server-sdk

### Q6 — Comment débugger un module plugin qui n'arrive pas à se connecter ?

> 1. Activer `LOG_LEVEL=debug` côté server-runtime
> 2. Vérifier l'Origin (si vous êtes en navigateur, passer par `localhost` ou configurer `AIRI_SERVER_CORS_ORIGINS`)
> 3. Vérifier le token (utiliser la comparaison timing-safe, pas de comparaison string)
> 4. Regarder les logs du Client sdk : `onError`, `onStateChange`
> 5. Inspecter le handshake WebSocket avec les devtools navigateur ou `wscat`

### Q7 — Qu'est-ce qui distingue un « service » d'un « plugin » dans AIRI ?

> Pour la distinction AIRI d'origine : un **service** est un process standalone (pas de hot-reload, pas de capabilities — ex: bots Discord, Telegram), un **plugin** a un cycle de vie complet géré par le plugin-host avec capability offer + permissions + hot-reload.
>
> **Côté Seren** la distinction n'existe plus : voir [09-plugins.md](09-plugins.md) pour l'équivalent unifié (`ISerenModule`).

### Q8 — Comment se fait la synchronisation multi-fenêtres dans stage-tamagotchi ?

> Chaque fenêtre Electron est un BrowserWindow avec son propre renderer. Elles communiquent avec le main process via `@moeru/eventa/electron-*`. Le main process maintient l'état global (via stores injectés ou via le server-runtime embarqué). Les changements sont propagés aux fenêtres concernées via `emit()` eventa.

### Q9 — Le serveur supporte-t-il plusieurs instances du même plugin ?

> Oui. Chaque `Client` génère un `instanceId` unique. Lors de `module:announce`, le serveur enregistre le peer sous `peersByModule.get(name).set(index, peer)`. L'index est attribué par le serveur (auto-incrément). Les consumer groups permettent le load-balancing.

### Q10 — Comment mettre à jour AIRI sur le poste utilisateur ?

> L'auto-updater (`electron-updater`) vérifie périodiquement le manifest YAML sur GitHub Releases. Selon le channel choisi (`stable|alpha|beta|nightly|canary`), il télécharge la mise à jour, puis propose à l'utilisateur de redémarrer. Le binaire est signé et notarisé (sur macOS) / signé (Windows).

## 16.7 Dépannage

### Problème : `pnpm i` échoue avec une erreur sur un patch

**Cause** : un fichier patch a été écrasé ou la version cible a changé.

**Solution** : vérifier `pnpm-workspace.yaml > patchedDependencies`. Si une dep a été upgradée sans regénérer son patch, refaire :
```bash
pnpm patch <package>
# appliquer modifs
pnpm patch-commit <temp-dir>
```

### Problème : Electron crash au démarrage (Linux/Wayland)

**Cause** : feature flags Chromium manquants pour Wayland.

**Solution** : le code `src/main/index.ts` ligne 63-78 active automatiquement `UseOzonePlatform` + `WaylandWindowDecorations` sur Linux/Wayland. Si le problème persiste, vérifier la version Electron et le binding du compositeur.

### Problème : Le client SDK ne se connecte pas, status reste `connecting`

**Cause possible 1** : Origin rejected par le serveur.
**Solution** : Ajouter l'origine à `AIRI_SERVER_CORS_ORIGINS`.

**Cause possible 2** : Timeout réseau.
**Solution** : Augmenter `connectTimeoutMs` dans `ClientOptions`.

**Cause possible 3** : WebSocket constructor incompatible.
**Solution** : Vérifier que `websocketConstructor` expose bien l'API standard (readyState, send, onmessage, onclose, onopen, onerror).

### Problème : Typecheck échoue avec « Cannot find module '@proj-airi/xxx' »

**Cause** : le package n'est pas encore built. Les packages internes dépendent de leur `dist/` pour l'import par d'autres workspaces.

**Solution** :
```bash
pnpm build:packages
# ou pour un package précis :
pnpm -F @proj-airi/xxx build
```

### Problème : Live2D model ne se charge pas dans le navigateur

**Cause** : Cubism SDK non téléchargé ou OPFS non autorisé.

**Solution** :
1. Vérifier que `@proj-airi/unplugin-fetch` a bien récupéré le SDK au build (dossier `public/cubism/`)
2. Vérifier les permissions OPFS (nécessite HTTPS ou localhost)
3. Consulter la console pour des erreurs de CORS

### Problème : Les animations VRM sont saccadées

**Cause** : WebGPU désactivé ou GPU trop lent.

**Solution** :
1. Vérifier dans `chrome://gpu` que WebGPU est actif
2. Sur Linux : activer `enable-unsafe-webgpu` et `enable-features=Vulkan`
3. Baisser la qualité de rendu (post-processing off)

### Problème : Les tests échouent avec « Cannot mock Electron »

**Cause** : `electron` n'est pas installé dans l'environnement de test.

**Solution** : utiliser `vi.mock('electron', () => ({ ... }))` en haut du fichier test. Voir exemple dans [13-tests-qualite.md § 13.5.1](13-tests-qualite.md).

## 16.8 Conventions de nommage

| Élément | Convention | Exemple |
|---------|-----------|---------|
| Fichiers | kebab-case | `use-chat-session.ts` |
| Composants Vue | PascalCase (dans le template, kebab-case dans le HTML) | `ChatPanel.vue` → `<chat-panel>` |
| Stores Pinia | `use<Name>Store` | `useOpenAIProviderStore` |
| Composables | `use<Name>` | `useAudioRecording` |
| Events protocol | `domain:subdomain[:verb]` | `input:text:voice`, `module:configuration:commit` |
| Eventa contracts | camelCase + préfixe domaine | `electronOpenSettings` |
| Types | PascalCase | `WebSocketEvent`, `ModuleIdentity` |
| Enums | PascalCase + UPPER_CASE members | `MessageHeartbeatKind.Ping` |
| Packages internes | `@proj-airi/<name>` | `@proj-airi/server-sdk` |
| Workspaces publics | `@moeru/<name>` | `@moeru/eventa` |

## 16.9 Comment contribuer

(Rappel des bonnes pratiques extraites de `AGENTS.md`)

1. **Fork + branche** : `username/feat/mon-feature`
2. **Commits Conventional** : `feat: add X`, `fix: correct Y`
3. **Petites PRs** : ≤ 400 lignes diff idéal
4. **Tests** : pour chaque bug, écrire un test de reproduction avant le fix
5. **Lint + typecheck** obligatoire avant push
6. **Pas de backward-compat** : si le refactor est gros, ouvrir une issue pour discussion d'abord
7. **README de chaque package** : maintenir à jour (pourquoi, quand l'utiliser, quand pas)

## 16.10 Contact et ressources communautaires

- **Discord** : https://discord.gg/TgQ3Cu2F7A
- **Twitter/X** : [@proj_airi](https://x.com/proj_airi)
- **Telegram** : [AIRI group](https://t.me/+7M_ZKO3zUHFlOThh)
- **GitHub Issues** : https://github.com/moeru-ai/airi/issues
- **Email équipe** : `airi@moeru.ai`

---

## 16.11 Licence

AIRI est publié sous licence **MIT** :

```
MIT License

Copyright (c) Moeru AI Project AIRI Team

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is furnished
to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 16.12 Historique des versions de ce document

| Version | Date | Auteur | Changement |
|---------|------|--------|-----------|
| 1.0 | 2026-04-15 | Documentation analysis | Création initiale couvrant l'analyse exhaustive du monorepo AIRI `0.9.0-rc.1` |

---

**Fin de la documentation exhaustive de Project AIRI.**
