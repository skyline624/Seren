# 01 — Vue d'ensemble du projet

## 1.1 Présentation générale

**Project AIRI** est une tentative open-source de re-créer un personnage virtuel IA complet, inspiré par **Neuro-sama**. Il s'agit d'un « conteneur d'âme » (soul container) pour un personnage virtuel capable de :

- **Dialoguer** en texte et en voix (reconnaissance vocale + synthèse vocale) ;
- **Être rendu à l'écran** sous forme d'un modèle 3D (VRM) ou 2D (Live2D) animé ;
- **Interagir avec des jeux** (Minecraft via mineflayer, Factorio) ;
- **Être déployé sur plusieurs plateformes** (desktop, web, mobile) ;
- **S'intégrer à des plateformes tierces** (Discord, Telegram, Bilibili, Twitter/X, Home Assistant) ;
- **Être étendu par des plugins** utilisateurs ou tiers.

Le projet est publié sous licence MIT par l'équipe **Moeru AI** (`moeru-ai`), avec le repository à `github.com/moeru-ai/airi`.

## 1.2 Philosophie et principes directeurs

Le projet adhère à plusieurs principes architecturaux explicites :

1. **Modularité par messages** : tous les composants actifs (frontend, services, bots, plugins) sont des **modules** qui communiquent via un bus WebSocket unique géré par un runtime serveur. Il n'y a pas de couplage direct entre un bot et un renderer.
2. **Typage strict de bout en bout** : TypeScript partout, avec Valibot pour la validation runtime (préféré à Zod dans ce projet), et des contrats d'événements partagés via `@proj-airi/plugin-protocol`.
3. **Injection de dépendances** : `injeca` est utilisé pour composer les services côté Electron main et côté backend. Les fonctions factory sont préférées aux classes.
4. **Programmation fonctionnelle + DI** : « *Avoid classes unless extending browser APIs; FP + DI is easier to test/mock.* » (AGENTS.md)
5. **Pas de compatibilité descendante paresseuse** : si un refactor casse quelque chose, il faut le réécrire proprement plutôt qu'ajouter des *fallbacks*. Les refactors importants doivent être documentés.
6. **UnoCSS préféré à Tailwind** : classes groupées dans des tableaux `v-bind:class="['...','...']"` pour la lisibilité.
7. **Eventa pour IPC** : `@moeru/eventa` est l'unique mécanisme type-safe pour le RPC/pub-sub, que ce soit entre process Electron ou entre frontend et server-runtime.
8. **Bundling léger** : `tsdown` pour les librairies, `electron-vite` pour l'app desktop, `Vite` pour les frontends web.

## 1.3 Public cible et cas d'usage

| Public | Cas d'usage |
|--------|-------------|
| Utilisateur final | Télécharger AIRI depuis les releases GitHub, la configurer avec un fournisseur LLM (OpenAI, Ollama, OpenRouter), et interagir via texte/voix avec un personnage VRM ou Live2D |
| Streamer | Intégrer AIRI à un overlay OBS, la faire réagir au chat Bilibili/Twitch, et la faire jouer à Minecraft en direct |
| Développeur IA | Ajouter de nouveaux fournisseurs LLM, créer des modules de mémoire, implémenter des pipelines vocaux |
| Développeur plugin | Écrire un plugin (JS/TS) qui se connecte au server-runtime pour ajouter une capability (ex: scraper une page web, contrôler IoT) |
| Contributeur open-source | Étudier un exemple de monorepo Vue/Electron/Capacitor complet à l'état de l'art 2026 |

## 1.4 Plateformes cibles

AIRI est distribuée en **trois surfaces** principales qui partagent le même cœur UI (`packages/stage-ui`) :

1. **Desktop (stage-tamagotchi)** : application **Electron** multi-fenêtres (chat, stage, settings, caption, widgets, about, onboarding…). Supportée sur Windows, macOS (Intel + Apple Silicon) et Linux (deb/rpm). Publiée via **electron-builder** avec **electron-updater** pour les mises à jour automatiques.
2. **Web (stage-web)** : **PWA Vue 3 + Vite** hébergée sur `airi.moeru.ai`. Utilise un *Service Worker* (vite-plugin-pwa) et des assets CDN via **WarpDrive** (S3-compatible).
3. **Mobile (stage-pocket)** : Wrapper **Capacitor 8** autour de la même base Vue, avec des ponts natifs iOS (Swift + `URLSession`) et Android (Kotlin + `OkHttp`) pour ouvrir une WebSocket vers le serveur AIRI (contourne les limitations de l'API WebSocket embarquée dans le WebView).

## 1.5 Technologies majeures

### Stack commune
- **TypeScript** 5.9, **Vue 3.5**, **Vue Router 5**, **Pinia 3**, **VueUse** 14.1
- **UnoCSS** 66 (avec `@proj-airi/unocss-preset-chromatic`)
- **Vite** 8, **tsdown** 0.21, **Turbo** 2.8
- **Valibot** 1.2, **Vitest** 4.1

### Stack IA / ML
- **xsai** (`@xsai/*`, 0.5.0-beta.2) — équivalent léger de Vercel AI SDK (Moeru AI, patches locaux)
- **HuggingFace Transformers** (JS), **ONNX Runtime Web**
- **Silero VAD** (`ricky0123/vad-web`) pour la détection d'activité vocale
- **Kokoro TTS** (worker)
- **wlipsync** pour la synchronisation labiale

### Stack 3D / 2D
- **Three.js** 0.183, **@pixiv/three-vrm** 3.5, **@tresjs/core** pour VRM
- **PIXI.js** 6.5, **pixi-live2d-display** 0.4 (patché) pour Live2D
- **Live2D Cubism SDK** (téléchargé par `@proj-airi/unplugin-fetch`)

### Stack serveur
- **H3** + **crossws** (WebSocket), **Hono** 4.11 (HTTP côté apps/server)
- **Drizzle ORM** + **PostgreSQL**, **@electric-sql/pglite** (embedded), **pgvector** (planifié)
- **better-auth** (OAuth/OIDC/passkeys) pour `apps/server`
- **superjson** 2.2 pour la sérialisation des enveloppes WebSocket
- **srvx** (patché) comme adapter HTTP universel

### Stack Electron
- **Electron** 40, **electron-vite** 5, **electron-builder** 26, **electron-updater** 6.8
- **injeca** 0.1.8 (DI container), **@moeru/eventa** 1.0.0-beta.3 (IPC/RPC)
- **@guiiai/logg** (logger avec formats et hooks)

### Stack Capacitor
- **Capacitor** 8 (core, ios, android), `@capacitor/local-notifications`, `@capacitor/barcode-scanner`
- **iOS** : Swift + `URLSession.WebSocketTask`
- **Android** : Kotlin + `OkHttp` WebSocket client

### Stack jeux / intégrations
- **mineflayer** 4.33 (Minecraft, patché), `isolated-vm` pour l'exécution sandboxée de scripts LLM
- **discord.js** + `@discordjs/voice` pour Discord
- **grammy** pour Telegram
- **Playwright** (Chromium) pour l'automatisation web (Twitter/X)

## 1.6 Valeurs et contraintes projet

- **Zéro dépendance propriétaire imposée** : l'utilisateur choisit ses fournisseurs LLM/TTS/STT.
- **Local-first** : stockage des données utilisateur prévu en local (DuckDB-WASM, OPFS pour Live2D, `config.json` pour Electron), même si `apps/server` existe pour les déploiements multi-utilisateurs.
- **Sécurité de base** : comparaison timing-safe pour les tokens d'auth (CWE-208), validation d'enveloppes à l'entrée WebSocket, rate-limiting par peer, CORS.
- **Internationalisation** : 9 langues supportées (`en`, `es`, `fr`, `ja`, `ko`, `ru`, `vi`, `zh-Hans`, `zh-Hant`), traductions centralisées dans `packages/i18n`.
- **Performance** : workers dédiés pour VAD, TTS, transcription ; rendu 3D via WebGPU (activé même sur Linux via *feature flags* Chromium).

## 1.7 État du projet au moment de l'analyse

- **Version** : `0.9.0-rc.1` (release candidate, cycle beta/rc)
- **Mainteneurs** : Moeru AI Project AIRI Team (contact : `airi@moeru.ai`)
- **Distribution binaire** : Windows `.exe`, macOS `.dmg`, Linux `.deb`/`.rpm` (voir README.md)
- **CI/CD** : GitHub Actions (dossier `.github/workflows/`)
- **Documentation publique** : `deepwiki.com/moeru-ai/airi`, site à `airi.moeru.ai`

## 1.8 Fonctionnalités principales (résumé)

1. **Orchestration LLM multi-fournisseurs** : OpenAI, Anthropic, OpenRouter, Aliyun NLS, Ollama, local providers
2. **Chat vocal duplex** : micro → VAD → STT → LLM → TTS → sortie audio + lipsync
3. **Rendu 3D VRM** (Three.js + three-vrm) ou **2D Live2D** (PIXI)
4. **Mémoire long-terme** (pgvector — en cours)
5. **Système de plugins** chargeable à chaud avec capabilities et permissions
6. **Gestion multi-fenêtres** Electron avec tray, dock, transparent, click-through
7. **Mises à jour automatiques** multi-canaux (stable, alpha, beta, nightly, canary)
8. **Onboarding** avec génération de QR code pour connecter un client web à un server-runtime local
9. **Système de widgets** extensibles à l'écran
10. **Sous-titres (caption window)** pour le streaming
11. **Détection d'émotion** et déclenchement d'expressions Live2D/VRM par *markers* dans le texte LLM
12. **Integration MCP** (Model Context Protocol) pour outils externes
