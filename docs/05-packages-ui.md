# 05 — Packages UI

Ce document couvre tous les packages liés à l'interface utilisateur, au rendu de l'avatar (VRM/Live2D), aux composants métier et à l'internationalisation.

## 5.1 `@proj-airi/stage-ui` — Noyau UI métier

### 5.1.1 Rôle

C'est le **cœur de l'expérience utilisateur** AIRI. Tous les composants, stores et composables spécifiques au domaine (LLM providers, modules, chat, caractère, audio) vivent ici. Les trois apps frontend (`stage-web`, `stage-tamagotchi`, `stage-pocket`) en dépendent.

### 5.1.2 Arborescence détaillée

```
packages/stage-ui/src/
├── components/
│   ├── scenarios/          # 9+ scenarios : about, chat, connection, dialogs, hologram, etc.
│   │   ├── about/
│   │   ├── chat/
│   │   ├── connection/
│   │   ├── dialogs/        # Dialog stacks / modals
│   │   ├── hologram/       # Scène « hologramme » (effet visuel spécial)
│   │   └── ...
│   ├── scenes/             # Scene wrappers (Three.js + Live2D)
│   ├── auth/               # Composants d'auth
│   ├── data-pane/          # Graphiques, visualisations
│   ├── gadgets/            # Petits widgets (clock, weather card, etc.)
│   ├── layouts/            # Layouts composant de haut niveau
│   ├── markdown/           # Rendu markdown (shiki, remark, rehype)
│   ├── modules/            # UI des modules AIRI
│   ├── graphics/           # Effets visuels (post-processing, shaders)
│   └── animations/         # Composants animés réutilisables
├── composables/
│   ├── audio/              # useAudioDevices, useAudioRecording, usePlayback
│   ├── use-chat-session/   # Session de chat (messages, scroll, TTS)
│   ├── vision/             # Vision / screen capture
│   ├── canvas-alpha.ts     # Test de transparence canvas
│   ├── markdown.ts         # Helpers markdown
│   ├── llm-marker-parser.ts # Parse les markers (actions, émotions) dans texte LLM
│   ├── use-auth-provider-sync.ts
│   ├── use-provider-validation.ts
│   ├── use-data-maintenance.ts
│   ├── response-categoriser.ts # Classe les chunks LLM en text/tool/action
│   └── ...
├── stores/
│   ├── providers.ts        # Export global des providers
│   ├── providers/          # Définitions standardisées des providers
│   │   ├── openai/
│   │   ├── openrouter/
│   │   ├── aliyun/
│   │   ├── elevenlabs/
│   │   ├── web-speech-api/
│   │   ├── ollama/
│   │   └── openai-compatible-builder.ts  # Builder pour créer un nouveau provider OpenAI-compatible
│   ├── modules/            # Modules AIRI orchestration
│   │   ├── hearing.ts      # STT + VAD orchestration
│   │   ├── speech.ts       # TTS synthesis
│   │   ├── consciousness.ts # Cognitive core (LLM appel principal)
│   │   ├── vision/         # Vision module
│   │   ├── gaming-minecraft.ts
│   │   ├── gaming-factorio.ts
│   │   ├── discord.ts
│   │   ├── twitter.ts
│   │   └── airi-card.ts    # Character card
│   ├── ai/
│   ├── chat/
│   ├── character/
│   ├── analytics/          # PostHog integration
│   ├── settings/
│   └── devtools/
├── workers/
│   ├── vad/                # Silero VAD worker
│   └── kokoro/             # Kokoro TTS worker
├── libs/
│   ├── audio/              # Utilitaires audio bas niveau
│   ├── providers/          # Provider builder helpers
│   └── workers/            # Worker utilities
├── types/                  # Types domaines partagés
├── constants/
│   └── prompts/            # Templates de prompts système
├── services/
│   └── speech/             # Services synthèse vocale
├── tools/
│   └── character/          # Outils manipulation caractère
├── database/
│   └── repos/              # Repositories données
└── assets/
    ├── vrm/                # Assets VRM (animations baked)
    └── live2d/             # Assets Live2D
```

### 5.1.3 Le store des providers

Exemple de structure d'un provider (`src/stores/providers/openai/index.ts`) :

```typescript
import { defineStore } from 'pinia'
import { createOpenAI } from '@xsai-ext/providers'

export const useOpenAIProviderStore = defineStore('provider-openai', () => {
  const apiKey = ref<string>('')
  const baseURL = ref<string>('https://api.openai.com/v1')
  const model = ref<string>('gpt-4o')

  const client = computed(() => createOpenAI({
    apiKey: apiKey.value,
    baseURL: baseURL.value,
  }))

  return { apiKey, baseURL, model, client }
})
```

Chaque provider expose une API unifiée (construction d'un client `@xsai-ext/providers`), ce qui permet aux modules (`hearing`, `speech`, `consciousness`) de rester agnostiques.

### 5.1.4 Les modules d'orchestration

Le fichier `src/stores/modules/consciousness.ts` est le **cœur du chat LLM**. Il écoute les événements `input:text` / `input:text:voice`, assemble le contexte (personnalité, mémoire, outils), appelle `streamText` via xsai, parse les chunks pour extraire markers (émotions, actions), et émet les chunks de réponse.

Un extrait schématique :

```typescript
export const useConsciousnessStore = defineStore('consciousness', () => {
  const providerStore = useOpenAIProviderStore()
  const characterStore = useCharacterStore()

  async function processInput(input: WebSocketEventInputText) {
    const messages = [
      { role: 'system', content: characterStore.systemPrompt },
      ...characterStore.history,
      { role: 'user', content: input.text },
    ]

    const stream = await streamText({
      model: providerStore.client(providerStore.model),
      messages,
      tools: characterStore.tools,
    })

    for await (const chunk of stream.textStream) {
      const parsed = parseLLMMarkers(chunk)
      // ...
      emitChunk(parsed)
    }
  }

  return { processInput }
})
```

### 5.1.5 Les composables audio

- `use-chat-session` : maintient l'état d'une session (messages, scroll, TTS queue)
- `audio/useAudioRecording` : démarre/arrête l'enregistrement, renvoie un `Blob`
- `audio/usePlayback` : jouer l'audio reçu de Kokoro ou ElevenLabs avec gestion de file
- `vision/useScreenCapture` : capture d'écran pour vision LLM
- `llm-marker-parser` : extrait les balises `<emotion:joy>`, `<action:wave>` etc. du flux LLM

### 5.1.6 Les workers

- **VAD worker** (`workers/vad/`) : utilise `@ricky0123/vad-web` (Silero en ONNX) pour détecter les frontières de parole en temps réel. Communique avec le thread principal via `postMessage`.
- **Kokoro worker** (`workers/kokoro/`) : synthèse vocale avec le modèle Kokoro. Produit du PCM Float32 + frames de visèmes pour lipsync.

### 5.1.7 Stories Histoire

`packages/stage-ui/histoire.config.ts` configure Histoire (storybook équivalent Vue). Exemple : `components/misc/Button.story.vue`. Lancer :

```bash
pnpm -F @proj-airi/stage-ui run story:dev
# ou depuis racine :
pnpm dev:ui
```

### 5.1.8 Build config

Contrairement à d'autres packages, `stage-ui` **n'a pas de build step** : ses consommateurs (stage-web, stage-tamagotchi) importent directement les sources TypeScript via les alias Vite. Cela accélère le dev mais impose que tous les consommateurs utilisent un bundler compatible (Vite ou équivalent).

La config Vite (`packages/stage-ui/vite.config.ts`) sert uniquement à Histoire et inclut WarpDrive pour les assets lourds.

---

## 5.2 `@proj-airi/ui` — Primitives reka-ui

### 5.2.1 Rôle

Primitives UI bas-niveau bâties sur **reka-ui** (v2.9) — l'équivalent Vue de shadcn/ui. Ce package ne contient **aucune logique métier** : juste des inputs, textareas, boutons, layouts.

### 5.2.2 Arborescence

```
packages/ui/src/
├── components/
│   ├── animations/         # Primitives d'animation
│   ├── form/               # Input, Textarea, Select, Checkbox, Radio (reka-ui wrapped)
│   ├── layouts/            # Container, Stack, Grid
│   └── misc/               # Button, Icon, Spinner, Link
├── composables/
├── constants/
├── main.css
└── index.ts
```

### 5.2.3 Exports

```json
{
  "exports": {
    ".": "./src/index.ts",
    "./*": "./src/*"
  }
}
```

Pattern wildcard : `import Button from '@proj-airi/ui/components/misc/Button.vue'`

### 5.2.4 Dépendances clés

- `reka-ui` 2.9.2 — primitives headless
- `floating-vue` — positionnement flottant (tooltips, popovers)
- `@vueuse/core` 14.1

---

## 5.3 `@proj-airi/stage-ui-three` — Three.js + VRM

### 5.3.1 Rôle

Fournit les composants Vue et composables qui gèrent le rendu 3D d'un modèle VRM (`.vrm`) dans Three.js, avec animations, expressions faciales, outlines et lipsync.

### 5.3.2 Arborescence

```
packages/stage-ui-three/src/
├── components/
│   ├── ThreeScene.vue      # Scene racine
│   ├── Controls/           # Camera + interaction controls
│   ├── Environment/        # Lighting, skybox, ground
│   └── Model/              # Model loading & manipulation
├── composables/
│   └── vrm/
│       ├── core.ts         # VRM loader, lifecycle
│       ├── animation.ts    # Animation VRM skeleton
│       ├── expression.ts   # Facial expressions (blendshapes)
│       ├── outline.ts      # Outline rendering (post-process)
│       └── lip-sync.ts     # Lipsync via wlipsync
├── stores/
│   └── model-store.ts      # Position, scale, active animations
├── assets/
│   └── vrm/
│       └── animations/     # Animations pré-cuites (fbx, vrma)
├── utils/
│   └── vrm-preview.ts
└── trace/                  # Debug / performance tracing
```

### 5.3.3 Dépendances principales

- `three` 0.183
- `@pixiv/three-vrm` 3.5.1 (loader VRM)
- `@tresjs/core` (renderer Vue 3 pour Three.js)
- `@tresjs/post-processing`
- `wlipsync` (lipsync auto-driven par l'audio)
- `culori` (manipulation couleur)

### 5.3.4 Exemple d'utilisation

```vue
<script setup lang="ts">
import { useVRM } from '@proj-airi/stage-ui-three/composables/vrm/core'
import { useLipSync } from '@proj-airi/stage-ui-three/composables/vrm/lip-sync'

const { vrm, loadVRM } = useVRM()
const { start, stop } = useLipSync(vrm)

await loadVRM('/assets/vrm/hiyori.vrm')
</script>

<template>
  <TresCanvas>
    <TresAmbientLight :intensity="1" />
    <primitive :object="vrm?.scene" />
  </TresCanvas>
</template>
```

---

## 5.4 `@proj-airi/stage-ui-live2d` — Live2D Cubism

### 5.4.1 Rôle

Wrapper autour de `pixi-live2d-display` (patché) pour intégrer des modèles Cubism Live2D (`.moc3`, `.model3.json`) dans une scène PIXI.js.

### 5.4.2 Arborescence

```
packages/stage-ui-live2d/src/
├── components/
│   └── scenes/
│       ├── Live2D.vue
│       └── live2d/
├── composables/
│   └── live2d/             # useLive2D, useModelLoader, useMotionTracker
├── stores/
│   ├── live2d.ts           # Position, scale, motions dispo
│   └── expression-store.ts # Paramètres expressions
├── utils/
│   ├── eye-motions.ts      # Eye tracking (mouse -> pupil)
│   ├── live2d-preview.ts
│   ├── live2d-zip-loader.ts  # Charge un ZIP de modèle
│   ├── live2d-opfs-registration.ts  # Stockage OPFS (Origin Private File System)
│   ├── live2d-uri-encode-filenames.ts
│   └── opfs-loader.ts      # Wrapper OPFS API
├── tools/
│   └── expression-tools.ts
└── constants/
    └── emotions.ts         # Mapping émotion → motion/expression
```

### 5.4.3 Dépendances

- `pixi-live2d-display` 0.4 (**patché**)
- `pixi.js` 6.5.10
- `jszip` (extraction des bundles modèle)
- `animejs` (tweening)
- `zod` (validation)
- Live2D Cubism SDK (téléchargé par `@proj-airi/unplugin-fetch` au build)

### 5.4.4 OPFS pour stockage offline

Le package implémente un système de stockage des modèles Live2D dans l'**Origin Private File System** (API navigateur). Cela permet de télécharger un modèle une fois et de le rejouer offline.

```typescript
import { registerLive2DOPFS } from '@proj-airi/stage-ui-live2d/utils/live2d-opfs-registration'

await registerLive2DOPFS('hiyori-pro', modelZipBlob)
```

Les fichiers internes (textures, json, moc3) sont extraits et stockés dans un dossier OPFS. Les noms sont URL-encodés pour éviter les collisions avec les caractères spéciaux.

---

## 5.5 `@proj-airi/stage-shared` — Utilitaires cross-stage

### 5.5.1 Rôle

Code partagé entre les trois apps frontend qui n'a pas sa place dans `stage-ui` (trop couplé UI) ni dans `ui` (spécifique au projet). Incluant :
- Helpers d'authentification
- Beat-sync (synchronisation animation avec rythme audio)
- Composables Electron (quand présent)
- QR code pour onboarding server channel
- Export CSV
- Environnement / feature detection

### 5.5.2 Exports

```json
{
  "exports": {
    ".": "./src/index.ts",
    "./auth": "./src/auth/index.ts",
    "./beat-sync": "./src/beat-sync/index.ts",
    "./composables": "./src/composables/index.ts",
    "./server-channel-qr": "./src/server-channel-qr.ts"
  }
}
```

### 5.5.3 `server-channel-qr.ts`

Génère/parse un payload QR code pour l'onboarding d'un client mobile vers un server-runtime local. Le payload contient une liste d'URLs candidates + token optionnel.

```typescript
export interface QrPayload {
  version: 1
  urls: string[]
  token?: string
  name?: string
}

export function encodeQrPayload(p: QrPayload): string { /* ... */ }
export function parseQrPayload(s: string): QrPayload { /* ... */ }
```

---

## 5.6 `@proj-airi/stage-layouts` — Layouts de stage

### 5.6.1 Rôle

Composants de layout pour le rendu d'une « stage » (scène interactive) : zone interactive, background, widgets, animations.

### 5.6.2 Contenu

- Composants Vue pour layouts (InteractiveArea, WidgetSlot, BackgroundVariant)
- Composables pour la gestion des widgets
- Stores Pinia pour le background et la mise en page
- Intégration avec `@xsai/generate-speech` pour les previews TTS
- Animations via `animejs`

### 5.6.3 Dépendances

- `@proj-airi/stage-ui`, `@proj-airi/ui`
- `@xsai/generate-speech`
- `pinia`, `vue-sonner`, `animejs`

---

## 5.7 `@proj-airi/stage-pages` — Templates de page

### 5.7.1 Rôle

Pages pré-assemblées (About.vue, Chat.vue, Connection.vue, Hologram.vue) qui composent plusieurs scénarios de `stage-ui`. Utilisées par les apps frontales pour éviter de dupliquer l'arrangement des composants.

### 5.7.2 Arborescence

```
packages/stage-pages/src/
├── About.vue
├── Chat.vue
├── Connection.vue
├── Hologram.vue
├── dialogs/          # Dialogs overlay
├── settings/         # Pages de settings (account, character, etc.)
├── providers/        # Pages de configuration providers
└── toasters/         # Notifications UI
```

### 5.7.3 Exports

Pattern wildcard :
```json
{
  "exports": {
    "./*.vue": "./src/*.vue",
    "./*": "./src/*"
  }
}
```

---

## 5.8 `@proj-airi/i18n` — Internationalisation

### 5.8.1 Rôle

Centralise toutes les traductions du projet. Les apps l'importent via `@proj-airi/i18n/locales`.

### 5.8.2 Structure

```
packages/i18n/src/
├── index.ts              # resolveSupportedLocale, localeRemap
└── locales/
    ├── en/               # Anglais (base)
    │   ├── common.yaml
    │   ├── chat.yaml
    │   ├── settings.yaml
    │   └── ...
    ├── es/               # Espagnol
    ├── fr/               # Français
    ├── ja/               # Japonais
    ├── ko/               # Coréen
    ├── ru/               # Russe
    ├── vi/               # Vietnamien
    ├── zh-Hans/          # Chinois simplifié
    └── zh-Hant/          # Chinois traditionnel
```

### 5.8.3 Fonctions clés

```typescript
// Résout une locale navigateur en une locale supportée avec fallback en
export function resolveSupportedLocale(navigatorLocale: string): SupportedLocale {
  // 'zh-CN' → 'zh-Hans'
  // 'en-US' → 'en'
  // 'xx' (inconnu) → 'en'
  return localeRemap[navigatorLocale] ?? /* fallback */ 'en'
}
```

### 5.8.4 Build

Utilise `tsdown` (pas Vite) pour compiler les modules locale. Les fichiers YAML sont chargés via `unplugin-yaml`.

### 5.8.5 Traductions via Crowdin

Le fichier `crowdin.yml` à la racine pilote la synchronisation des traductions via le service Crowdin. Les fichiers de référence sont les YAMLs en `en/`.
