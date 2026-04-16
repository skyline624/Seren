# 07 — Packages infrastructure

Ce document couvre les packages utilitaires et d'infrastructure qui ne relèvent ni de l'UI, ni du serveur, mais qui alimentent les pipelines audio, le rendu, la persistance, et l'outillage de build.

## 7.1 Pipelines audio

### 7.1.1 `@proj-airi/audio`

Utilitaires Web Audio de bas niveau.

- **Entrée** : `./dist/index.mjs`
- **Modules** :
  - `audio-context` — wrapper `AudioContext` + **AudioWorklet** (pour capture 48kHz hors main thread)
  - `encoding` — resampling via `libsamplerate-js`
- **Dépendances** : `@alexanderolsen/libsamplerate-js`, `@moeru/std`

```typescript
import { createAudioContext, resampleFloat32 } from '@proj-airi/audio'

const ctx = createAudioContext({ sampleRate: 48000 })
const resampled = resampleFloat32(input, 48000, 16000)  // pour Whisper
```

### 7.1.2 `@proj-airi/pipelines-audio`

Pipeline audio orchestrée : **capture → VAD → encode → speech queue → playback**.

- **Entrée** : `./dist/index.mjs`
- **Modules** :
  - `speech-pipeline` — orchestration globale (capture ↔ VAD ↔ STT ↔ LLM ↔ TTS)
  - `eventa` — contrats eventa pour les événements de pipeline
  - `playback-manager` — queue de lecture audio avec priorités et interruptions
  - `tts-chunker` — découpe le stream LLM en chunks parlables (phrase par phrase)
  - `stream-types` — types ReadableStream custom
- **Dépendances** : `@moeru/eventa`, `@moeru/std`, `clustr`

Le **playback manager** est clé : il gère les interruptions (si l'utilisateur parle pendant que AIRI répond), la synchronisation avec les animations de bouche, et la mise en queue des chunks TTS.

### 7.1.3 `@proj-airi/audio-pipelines-transcribe`

Utilitaires spécifiques à la transcription (phase 1 : stub). Supporte l'intégration avec les modèles Whisper via `@xsai/generate-transcription`.

### 7.1.4 `@proj-airi/stream-kit`

Très petit package (aucune dep externe), fournit une queue observable légère utilisée par les pipelines audio :

```typescript
import { createQueue } from '@proj-airi/stream-kit'

const queue = createQueue<Chunk>()
queue.push(chunk1)
queue.push(chunk2)
for await (const chunk of queue) {
  process(chunk)
}
```

---

## 7.2 Drivers modèles

### 7.2.1 `@proj-airi/model-driver-lipsync`

Wrapper autour de `wlipsync`, qui génère des paramètres de visèmes à partir d'un flux audio pour animer la bouche d'un modèle VRM ou Live2D.

- **Entrée** : `./src/index.ts`
- **Intégration** : Live2D (via stores Live2D) et Three VRM (via expressions VRM)
- **Dep** : `wlipsync`

### 7.2.2 `@proj-airi/model-driver-mediapipe`

Driver de **motion capture** utilisant MediaPipe Tasks Vision (Google). Détecte pose, mains, visage depuis une webcam et applique les mouvements à un VRM.

- **Entrée** : `./src/index.ts`
- **Modules** :
  - Backends MediaPipe (pose, hands, face)
  - Engine pour orchestrer les trois backends
  - Three.js utilities (mapping pose → VRM skeleton)
  - Overlay utilities (visualisation debug)
- **Deps** :
  - `@mediapipe/tasks-vision` (**patché** localement)
  - `@pixiv/three-vrm`, `three`
  - `es-toolkit`

---

## 7.3 Mémoire et caractère

### 7.3.1 `@proj-airi/memory-pgvector`

Mémoire vectorielle long-terme utilisant PostgreSQL + l'extension **pgvector**.

- **Statut** : WIP (stub)
- **Plan** : fournir un client `MemoryClient` qui expose `store(text, metadata)` et `query(text, topK)`, connecté à un schema Drizzle avec colonne `vector(1536)`.
- **Deps** : `drizzle-orm`, `postgres`, `@proj-airi/server-sdk`

### 7.3.2 `@proj-airi/core-character`

Pipeline de traitement des traits caractère (segmentation, détection d'émotion, orchestration TTS). Actuellement un **stub** — la logique vit dans `stage-ui/stores/modules/consciousness.ts`.

- **Deps** : `@proj-airi/stream-kit`, `clustr`, `@moeru/std`

---

## 7.4 Intégration Electron

### 7.4.1 `@proj-airi/electron-eventa`

Contrats eventa prêts à l'emploi pour les besoins courants Electron :
- IPC main ↔ renderer (invoke + emit)
- Events `electron-updater` typés

- **Entrée** : `./dist/index.mjs`
- **Deps** : `@moeru/eventa`, `builder-util-runtime` (pour les types d'auto-updater)

### 7.4.2 `@proj-airi/electron-screen-capture`

Capture d'écran via `desktopCapturer` d'Electron avec bindings eventa :

- `initScreenCaptureForMain()` — s'enregistre dans le main process et gère les sources disponibles
- Contrats eventa : `getSources`, `setSource`, `resetSource`
- **Utilisé par** : `apps/stage-tamagotchi/src/main/index.ts` ligne 84

### 7.4.3 `@proj-airi/electron-vueuse`

VueUse-like composables pour Electron. Permet à un composant Vue du renderer d'observer l'état OS :

```typescript
import { useElectronWindowBounds, useElectronMouse, useElectronAutoUpdater } from '@proj-airi/electron-vueuse'

const { bounds } = useElectronWindowBounds()
const { position } = useElectronMouse()
const { status, downloadProgress } = useElectronAutoUpdater()
```

- **Déps** : `@moeru/eventa`, `@vueuse/core`, `es-toolkit`
- **Turbo** : dépend explicitement de `@proj-airi/electron-eventa#build`

---

## 7.5 Base de données

### 7.5.1 `@proj-airi/drizzle-duckdb-wasm`

Adaptateur Drizzle ORM pour **DuckDB WASM** (base analytique embarquée dans le navigateur).

- **Statut** : placeholder

### 7.5.2 `@proj-airi/duckdb-wasm`

Wrapper minimal autour de `@duckdb/duckdb-wasm`.

- **Statut** : placeholder

Ces deux packages sont prévus pour offrir à l'utilisateur une base de données analytique locale (historique de chat, statistiques, mémoire) sans serveur externe.

---

## 7.6 Outillage de build

### 7.6.1 `@proj-airi/vite-plugin-warpdrive`

Plugin Vite qui **uploade automatiquement les assets lourds** (`.vrm`, `.wasm`, `.ttf`, `.moc3`) vers un bucket S3-compatible pendant le build et **réécrit les URLs** dans le bundle pour pointer vers le CDN.

- **Entrée** : `./dist/index.mjs`
- **Deps** :
  - `rolldown` (le nouveau bundler Rust de la team Rolldown/oxc, utilisé par Vite 8 en interne)
  - `s3mini` (client S3 minimal, 6KB)
  - `@moeru/std`
- **Exports** : `WarpDrivePlugin`, provider S3, types

Exemple d'usage dans `vite.config.ts` :

```typescript
import { WarpDrivePlugin } from '@proj-airi/vite-plugin-warpdrive'

export default defineConfig({
  plugins: [
    WarpDrivePlugin({
      enabled: process.env.NODE_ENV === 'production',
      provider: {
        type: 's3',
        bucket: 'airi-assets',
        endpoint: 'https://s3.amazonaws.com',
        accessKeyId: process.env.AWS_ACCESS_KEY_ID,
        secretAccessKey: process.env.AWS_SECRET_ACCESS_KEY,
      },
      include: ['**/*.vrm', '**/*.wasm', '**/*.ttf', '**/*.moc3'],
    }),
  ],
})
```

Avantages : le bundle Vite final ne contient pas ces fichiers (qui pèseraient des dizaines voire centaines de Mo), et les clients les téléchargent directement depuis le CDN.

### 7.6.2 `@proj-airi/unocss-preset-fonts`

Preset UnoCSS qui injecte les fontes du projet comme font-family utilities (`.font-airi`, `.font-jp`, etc.).

### 7.6.3 `@proj-airi/unplugin-fetch` (utilisé mais externe)

Plugin unplugin qui télécharge automatiquement les assets externes au build (Cubism SDK, VRM samples). Pas dans le monorepo mais dépendance critique.

---

## 7.7 UI utilitaires

### 7.7.1 `@proj-airi/ui-loading-screens`

Collection de composants Vue de loading screens animés (utilisant `@rive-app/canvas-lite` pour les animations Rive).

- **Usage** :
```vue
<script setup>
import { LoadingRiveAnimation } from '@proj-airi/ui-loading-screens'
</script>

<template>
  <LoadingRiveAnimation src="/assets/rive/loading.riv" />
</template>
```

### 7.7.2 `@proj-airi/ui-transitions`

Composants Vue de transitions animées (fade, slide, scale, custom) bâtis sur `@vueuse/motion`.

---

## 7.8 Testing visuel

### 7.8.1 `@proj-airi/vishot-runtime`, `vishot-runner-browser`, `vishot-runner-electron`

Outils de **visual regression testing** développés en interne. Comparent des screenshots de scènes AIRI (Live2D, VRM, UI) avec des baselines pour détecter les régressions visuelles.

- `vishot-runtime` — code commun
- `vishot-runner-browser` — headless browser (probablement Playwright)
- `vishot-runner-electron` — Electron scripts pour capturer la fenêtre
- `scenarios-stage-tamagotchi-browser` / `scenarios-stage-tamagotchi-electron` — scénarios de test

Le script racine `capture:tamagotchi` déclenche un run :

```bash
pnpm capture:tamagotchi
```

Cela exécute `packages/scenarios-stage-tamagotchi-electron/src/scenarios/demo-controls-settings-chat-websocket.ts` et sauvegarde les captures dans `packages/scenarios-stage-tamagotchi-browser/artifacts/raw`.

---

## 7.9 Capacitor tooling

### 7.9.1 `@proj-airi/cap-vite`

Plugin/outil custom pour intégrer Capacitor avec Vite (pont dev server / build sync). Utilisé par `apps/stage-pocket`.

- **Scripts fournis** : `dev:ios`, `dev:android` (ces scripts de stage-pocket appellent cap-vite)
- Synchronise le `dist/` avec les projets natifs `ios/` et `android/`
- Gère le port Vite pour que Capacitor le serve sur localhost

---

## 7.10 Fontes

Quatre packages fonte :

- `@proj-airi/font-chillroundm` — police arrondie utilisée pour les titres
- `@proj-airi/font-cjkfonts-allseto` — fontes CJK (Chinois/Japonais/Coréen)
- `@proj-airi/font-departure-mono` — monospace
- `@proj-airi/font-xiaolai` — police fallback zh

Chaque package contient les fichiers `.woff2` et `.ttf` plus un CSS d'import (`@font-face`). Ils sont souvent offloadés via WarpDrive.

---

## 7.11 Résumé tabulaire

| Package | Rôle | Statut |
|---------|------|--------|
| audio | Web Audio bas niveau | ✅ stable |
| audio-pipelines-transcribe | Transcription helpers | 🚧 stub |
| pipelines-audio | Pipeline orchestration | ✅ stable |
| stream-kit | Queue/stream | ✅ stable |
| model-driver-lipsync | Lipsync wlipsync | ✅ stable |
| model-driver-mediapipe | Mocap MediaPipe | ✅ stable |
| memory-pgvector | Mémoire vectorielle | 🚧 WIP |
| core-character | Character pipeline | 🚧 stub |
| electron-eventa | Eventa contracts | ✅ stable |
| electron-screen-capture | desktopCapturer | ✅ stable |
| electron-vueuse | Composables Electron | ✅ stable |
| drizzle-duckdb-wasm | DuckDB WASM adapter | 🚧 placeholder |
| duckdb-wasm | DuckDB wrapper | 🚧 placeholder |
| vite-plugin-warpdrive | Asset CDN | ✅ stable |
| ui-loading-screens | Loading screens | ✅ stable |
| ui-transitions | Transitions Vue | ✅ stable |
| vishot-runtime | Visual testing | ✅ stable |
| vishot-runner-browser | Visual testing browser | ✅ stable |
| vishot-runner-electron | Visual testing electron | ✅ stable |
| scenarios-*-browser/electron | Scénarios tests | ✅ stable |
| cap-vite | Capacitor+Vite | ✅ stable |
| font-* (×4) | Polices | ✅ stable |
| unocss-preset-fonts | UnoCSS preset fontes | ✅ stable |
