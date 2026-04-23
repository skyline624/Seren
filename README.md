# Seren

> Real-time coordination hub for an embodied AI character — multi-surface chat with a VRM or Live2D avatar.

Seren is a thin coordinator in front of an LLM backend. It serves a Vue 3 web PWA (with Tauri desktop and Capacitor mobile targets), terminates WebSocket connections from every surface on a single `.NET 10` hub, and delegates all model traffic to an OpenClaw gateway. The avatar, the chat history, the settings, and the model routing live in this repository; LLM providers are pluggable.

## Feature status

- ✓ Streaming text chat over WebSocket, with emotion and action markers extracted from the stream.
- ✓ Multi-device conversation history, persistent across reloads and peer devices on the same server session.
- ✓ Six-section settings drawer (Connection / Appearance / Avatar / Voice / LLM / Character), persisted per-key in `localStorage`.
- ✓ VRM and Live2D rendering, with eleven live tuning knobs on the VRM path (scale, position, rotation, camera, lighting, eye tracking, toon outline).
- ✓ **Five-layer avatar animation pipeline** — base `.vrma` clip + additive body sway + `<action:*>` clips queued FIFO + face layer (auto-blink, eye saccades, emotion blendshapes with cubic easing) + phase-based state machine (`idle` / `listening` / `thinking` / `talking` / `reactive`) modulating each layer's gain. Aligned with the VRChat Playable Layers / Warudo blueprint.
- ✓ Server-side model catalogue merging OpenClaw's cloud providers with locally-installed Ollama models at `GET /api/models`.
- ✓ Character library — CRUD + Character Card v3 import (JSON / PNG / APNG) + OpenClaw-workspace persona capture + one-click JSON download for backup and transfer.
- ✓ Typed error popup on chat failures (idle timeout, auth, model-not-found, …) — no cross-model silent fallback, the user's chosen model is respected.
- ◦ Voice pipeline (VAD, STT, TTS) — scaffolded but dormant in the current release.
- ◦ Plugin / module system — on the roadmap.

## Roadmap

**Shipped**

- Persistent WebSocket hub with authenticated handshake (`authenticate → announce → registry:sync`).
- Streaming text chat with chunk-level `<think>` filtering and `<emotion:*>` / `<action:*>` marker extraction.
- Multi-device conversation history, server-side session rotation for "new conversation" (preserves long-term memory).
- Settings drawer with six sections (Connection, Appearance, Avatar, Voice, LLM, Character), persisted per-key in `localStorage`.
- Theme (auto / light / dark) and free primary-hue slider driving CSS custom properties at `:root`.
- Locale switching (fr / en) wired to `vue-i18n`.
- VRM renderer with reactive scale, position, rotation, camera distance / height / FOV, ambient + directional lighting, eye tracking (camera / pointer / off), toon outline (thickness, colour, alpha).
- Live2D fallback viewer for 2D models.
- **Avatar animation pipeline (5 layers)** :
  - Base `.vrma` clip via `AnimationMixer` (`idle_loop.vrma` looping).
  - Additive body sway — three phase-shifted sinusoids on `spine` / `chest` / `hips` composed via `quaternion.multiply` (amplitudes aligned with Animaze).
  - Action clip layer with FIFO queue (max 3) and return-to-idle timing derived from the real clip duration.
  - Face layer — auto-blink (0.2 s sine over 1-6 s cadence), eye saccades (±0.25 world unit jitter every 0.4-2.5 s), emotion blendshapes with cubic easing + auto-reset to neutral, alias resolution for hub emotion names (`happy`/`happiness`/`joy` → `joy`, …), intensity driven by the Tier-2 text classifier's confidence score.
  - Per-phase state machine derived from the chat store — `idle` / `listening` / `thinking` / `talking` / `reactive` — multiplying each layer's gain (e.g. `thinking` damps sway, slows blink, quickens saccades, adds a small head-forward tilt).
- Character library — CRUD with inline active-character editor, Character Card v3 import (JSON / PNG / APNG with embedded `character_book`), OpenClaw-workspace persona capture (`POST /api/characters/capture`), per-character JSON download (`GET /api/characters/{id}/download`).
- Chat resilience — typed error popup dialog with localised per-code messaging (idle timeout, auth, model-not-found, …), retry affordance on transient failures, no cross-model silent fallback.
- `/api/models` endpoint merging OpenClaw's cloud catalog and the local Ollama `GET /api/tags` with a 60-second server cache.
- Chat stream handler detached from the WebSocket receive loop so heartbeats, reset commands, and scroll-back requests stay responsive during long-running LLM responses.
- Docker Compose orchestration bringing up OpenClaw, Seren API, and Seren Web together.

**In progress**

- Light-theme CSS cascade fix across the Settings drawer and `<html lang>` synchronisation with the locale store.
- PWA asset set (192×192, 512×512 icons, maskable variant) and offline precache regression fix.
- Multi-tab synchronisation for user messages (assistant replies are already broadcast; user echo still missing on peer tabs).
- Thinking-mode plumbing from the LLM settings store through OpenClaw's session config.

**Planned — next**

- Voice pipeline end-to-end: VAD threshold + sensitivity, STT provider catalog (confidence-threshold slider for Whisper-family providers), TTS provider catalog (voice, pitch, speed), queued playback with concurrency control, barge-in.
- Expand the `.vrma` action pack beyond `wave` / `think` / `pixiv_demo` (drop in `nod`, `bow`, `shake`, `look_around`, `stretch`, …) — workflow documented in `src/ui/apps/seren-web/public/animations/NOTICE.md` (Mixamo → `fbx2vrma-converter`, VRoid Hub motions, commission).
- Scene / backdrop selector for the avatar stage.
- Conversation export / import (CSV or Markdown).
- Resource island: floating widget showing STT / VAD / VRM asset download progress and dependencies per enabled module.

**Planned — medium-term**

- Module / plugin system: event-bus registration where modules declare capabilities, scopes, required permissions, and UI widgets; delivery modes (broadcast, consumer-group) with selection strategies (round-robin, sticky, priority); hot-reload.
- Permission / approval UI for module scopes (`invoke`, `emit`, `read`, `write`, `subscribe`, `register`, `execute`, `hook`, `process`, `manage`) with i18n-aware reason strings surfaced in the consent dialog.
- Long-term memory UI: distinction between ephemeral session context and per-module persistent storage, with retrieval-augmented recall driven by an embedding model.
- Audio-reactive avatar: beat-sync worklet (spectral flux, adaptive threshold, buffer duration, highpass / lowpass / envelope filters, BPM bounds) and finer-grained lip-sync driven by WebGPU-accelerated ONNX inference.
- Per-agent tool declaration so tool-capable models (Gemma 4, Qwen3) expose curated function schemas rather than relying on the default gateway catalog.

**Later**

- Channel integrations: Discord (voice presence + audio pipeline), Telegram (text + image + sticker + tool calling), Satori for universal bot platforms (Kook, Lark, Slack, DingTalk), Twitter monitoring, Minecraft cognitive agent. Each channel is gated behind its own provider credentials and a feature flag.
- Desktop-shell controls (always-on-top, compact controls island, visible-on-all-workspaces) once the Tauri app stabilises.
- Native mobile recording and playback pipeline (Capacitor plugin) once the voice pipeline is in place.
- In-UI observability: OpenTelemetry trace stream viewer, devtools IO tracer for the WebSocket envelopes.
- Settings / memory backup: unified export / import of `localStorage` + per-module persistent stores + character library into a single signed archive.

## Tech stack

- **Server** — .NET 10 / ASP.NET Core, Clean Architecture (Domain / Application / Infrastructure / Server.Api), Mediator source generator, FluentValidation, Serilog, OpenTelemetry.
- **Web** — Vue 3, Pinia, UnoCSS, `vue-i18n`, Vite + PWA plugin.
- **Desktop** — Tauri 2.
- **Mobile** — Capacitor 8 (iOS, Android).
- **3D avatar** — TresJS (Three.js Vue bindings), `@pixiv/three-vrm`, `@pixiv/three-vrm-animation`.
- **2D avatar** — PIXI.js + `pixi-live2d-display` (Cubism 4).
- **LLM backend** — OpenClaw gateway fronting Ollama, OpenAI, Anthropic, Google, Meta, Qwen, GLM, Mistral and more.
- **Local acceleration** — Ollama with AMD ROCm (kernel 6.8 + `amdgpu-dkms` + ROCm 7) or NVIDIA CUDA.

## Architecture

```
┌─────────────────────────┐
│  Web / Desktop / Mobile │
│   Vue 3 clients (SDK)   │
└───────────┬─────────────┘
            │ WebSocket (/ws)  +  REST (/api)
            ▼
┌─────────────────────────┐
│       Seren Hub         │   .NET 10, WebSocket hub, /api/models, /api/characters
│ (Clean Architecture)    │   In-memory peer registry, session key rotation
└───────────┬─────────────┘
            │ WebSocket (gateway protocol, auth + scopes)
            ▼
┌─────────────────────────┐
│    OpenClaw Gateway     │   LLM routing, persisted transcripts, plugins, agents
└───────────┬─────────────┘
            │
    ┌───────┼────────┬────────────┬───────────┐
    ▼       ▼        ▼            ▼           ▼
 Ollama   OpenAI  Anthropic     Google     Other providers
 (local)                                   (Qwen, Groq, GLM, …)
```

Seren owns the user surface, the realtime transport, the character library, and the per-device identity. OpenClaw owns the LLM session, the memory plugins, the tool execution, and the provider credentials. Both communicate over a single authenticated WebSocket and a handful of REST endpoints.

## Quick start (Docker Compose)

```bash
git clone https://github.com/<your-fork>/seren
cd seren
cp .env.example .env
# Edit .env and set OPENCLAW_GATEWAY_TOKEN to a strong random string.
docker compose up -d --build
```

Wait for the three containers (`openclaw`, `seren-api`, `seren-web`) to report `healthy`, then open `http://localhost:9080`. The web PWA loads the default VRM avatar, connects to the hub, and is ready to chat as soon as at least one provider is configured in `ops/openclaw/openclaw.json` or through environment variables in `.env`.

To route traffic to a locally-installed Ollama, make sure Ollama is bound on `0.0.0.0:11434` on the host (e.g. through a systemd override) and leave `OLLAMA_BASE_URL` at its default (`http://host.docker.internal:11434`). Installed Ollama models appear under the `ollama/` provider in Settings → LLM after the next refresh.

## Local development

Each side builds independently — there is no shared build at the repo root.

**Server**

```bash
dotnet restore src/server/Seren.sln
dotnet build   src/server/Seren.sln
dotnet test    src/server/Seren.sln
dotnet run --project src/server/Seren.Server.Api
```

**UI**

```bash
cd src/ui
pnpm install
pnpm build:packages
pnpm -F @seren/web dev          # PWA on http://localhost:5173
pnpm -F @seren/desktop tauri dev
pnpm -F @seren/mobile cap:android
```

Workspace packages use `"workspace:*"` references and the `catalog:` versions pinned in `src/ui/pnpm-workspace.yaml`. Turbo orchestrates `build` → `typecheck` → `test` chains with `^build` dependencies, so changing a package rebuilds its dependents on demand.

## Testing

```bash
# Server — xUnit v3 + Shouldly + NSubstitute
dotnet test src/server/Seren.sln
dotnet format src/server/Seren.sln --verify-no-changes

# UI — vitest across the pnpm workspace
pnpm -C src/ui test
pnpm -C src/ui typecheck
pnpm -C src/ui lint
```

The server suite currently ships ~230 tests across Domain / Application / Infrastructure / Server.Api.IntegrationTests; the UI suite ships ~140 vitest tests covering the shared stores (chat, avatar state, settings, character), composables (idle scheduler, blink, saccades, emote, body sway, emotion classifier, persisted ref), and the SDK client. Every procedural-animation composable accepts injected `random` / `now` dependencies so tests pin outcomes deterministically without touching global time.

## User-facing settings

The settings drawer exposes six sections, all persisted per-key in `localStorage` under the `seren/` namespace:

- **Connection** — server URL override, auth token.
- **Appearance** — theme mode (auto / light / dark), primary hue (0-360°), interface locale.
- **Avatar** — mode picker (VRM / Live2D), model scale, position, rotation, camera distance / height / FOV, ambient + directional lighting, eye tracking (camera / pointer / off), toon outline (thickness, colour, alpha).
- **Animation IA** — idle-scheduler cadence (slow / normal / fast), in-browser text-emotion classifier toggle (~66 MB one-time download, opt-in), auto-blink / eye-saccade / body-sway toggles for users who prefer a still avatar.
- **Voice** — voice-detection threshold (remaining knobs ship with the voice pipeline).
- **LLM** — provider picker, model dropdown populated by `/api/models`, custom model ID override, thinking mode (stored).
- **Character** — active character display with inline edit of name, agent ID, system prompt, VRM asset path ; "Import CCv3 card" button (JSON/PNG/APNG), "Capture OpenClaw persona" button (`POST /api/characters/capture`), "Download" button on the active character (JSON serialised via the source-gen context at `GET /api/characters/{id}/download`).

## LLM providers

`GET /api/models` returns a merged catalogue built server-side from two sources:

- **OpenClaw cloud** — Anthropic, Google Bedrock, Meta, Qwen, GLM, Mistral, MiniMax and more (hundreds of entries depending on gateway config).
- **Ollama local** — `ollama/<name>` entries sourced from the Ollama daemon's `GET /api/tags`, covering both locally-installed weights (e.g. `ollama/seren-qwen:latest`, `ollama/seren-gemma:latest`) and cloud-routed Ollama models (`ollama/qwen3.5:cloud`, `ollama/nemotron-3-super:cloud`, …).

For AMD systems running sizable local models (Qwen3 9B, Gemma 4 7.5B, etc.), the ROCm-accelerated Ollama path is supported on a generic Linux 6.8 kernel with `amdgpu-dkms` and ROCm 7. Detailed setup notes live in `./docs/14-guide-reproduction.md`.

### Chat resilience

Seren deliberately does **not** cascade to a different model on failure — the user's chosen provider is respected. Transient stream hiccups trigger at most one same-model retry (`OpenClaw:Chat:Resilience:RetryOnIdleBeforeFirstChunk`) ; anything further surfaces through a first-class `ChatErrorDialog` popup with a typed error code (`idle_timeout`, `total_timeout`, `auth`, `model_not_found`, `unknown`), a localised remediation hint, a "Change model" shortcut that opens the Settings drawer, and — on transient errors — a one-click "Retry" that resends the last user message. Error taxonomy lives in the `ErrorPayload` wire contract ; UI mapping is in `src/ui/packages/seren-ui-shared/src/components/ChatErrorDialog.vue`.

## Avatar animation pipeline

The VRM renderer composes five concurrent layers on every frame, in canonical three-vrm order:

```
mixer.update(delta)            // (1) base idle.vrma writes node.quaternion
bodySway.update(vrm, delta)    // (2) additive sinusoids on spine / chest / hips
emote.update(delta)            // (3) cubic-eased emotion blendshapes
blink.update(vrm, delta)       // (4) sine-curve eyelid — 1-6 s jittered cadence
saccade.update(vrm, δ)         // (5) moves vrm.lookAt.target position
vrm.update(delta)              // (6) resolves humanoid + lookAt + expressions + spring bones + MToon materials
```

Pattern cross-validated against [VRChat Playable Layers](https://creators.vrchat.com/avatars/playable-layers/) (Base / Additive / Gesture / Action / FX), [Warudo's layered composition](https://docs.warudo.app/docs/assets/character), Animaze (scale 1→1.025 chest breath) and AIRI's stage-ui-three composables.

A per-phase state machine derived from the chat store modulates every layer's gain :

| Phase      | Body sway | Blink freq | Saccade freq | Head tilt |
|------------|-----------|------------|--------------|-----------|
| `idle`     | ×1.0      | ×1.0       | ×1.0         | 0         |
| `listening`| ×0.9      | ×1.0       | ×0.8         | 0         |
| `thinking` | ×0.6      | ×0.7       | ×1.4         | −0.12 rad |
| `talking`  | ×1.2      | ×1.1       | ×0.9         | 0         |
| `reactive` | ×1.0      | ×1.0       | ×1.0         | 0         |

Phase resolution priority : `talking > thinking > reactive > listening > idle`. Every layer's constants (intervals, amplitudes, durations, emotion presets, aliases, phase-gain table) are exported named constants — tweak `BREATH_AMPLITUDE_DEFAULT` / `BLINK_MIN_INTERVAL` / `PHASE_GAINS.thinking.bodySway` in one place, it propagates everywhere.

Composables live in `src/ui/packages/seren-ui-three/src/composables/` (`useBlink`, `useIdleEyeSaccades`, `useVRMEmote`, `useIdleBodySway`) ; the state machine + layer gains live in `src/ui/packages/seren-ui-shared/src/stores/avatarState.ts` and `composables/useAvatarLayerGains.ts`.

### Adding new `.vrma` action clips

Drop a `.vrma` file into `src/ui/apps/seren-web/public/animations/`, register it in `DEFAULT_ACTION_CLIPS` (triggered on demand by `<action:name>` markers) or `DEFAULT_IDLE_CLIPS` (auto-fired during pauses) inside `src/ui/packages/seren-ui-shared/src/components/AvatarStage.vue`. The idle scheduler catalog is data-driven from the map's keys — no code change. Sourcing paths + licensing notes (Mixamo, VRoid Hub, pixiv demo, commission) are documented in `src/ui/apps/seren-web/public/animations/NOTICE.md`.

## Repository layout

```
Seren/
├── src/
│   ├── server/                 # .NET 10 solution (Seren.sln)
│   │   ├── Seren.Domain/       # Entities, value objects, abstractions — no framework deps
│   │   ├── Seren.Application/  # Use cases as Mediator IRequest / IRequestHandler pairs
│   │   ├── Seren.Contracts/    # Wire-level DTOs + EventTypes shared with the SDK
│   │   ├── Seren.Infrastructure/  # OpenClaw / Ollama adapters, realtime hub, auth
│   │   ├── Seren.Server.Api/   # ASP.NET Core composition root, Dockerfile
│   │   └── tests/              # Domain / Application / Infrastructure / Integration
│   └── ui/                     # pnpm workspace + Turbo
│       ├── apps/
│       │   ├── seren-web/      # Vue 3 PWA (Vite)
│       │   ├── seren-desktop/  # Tauri 2 shell
│       │   └── seren-mobile/   # Capacitor 8 shell
│       └── packages/
│           ├── seren-sdk/      # Typed WebSocket client + contracts
│           ├── seren-ui-shared/   # Vue components + pinia stores
│           ├── seren-ui-three/    # VRM renderer (TresJS + three-vrm)
│           ├── seren-ui-live2d/   # Live2D renderer (PIXI + pixi-live2d-display)
│           ├── seren-ui-audio/    # VAD + audio pipeline primitives
│           └── seren-i18n/     # Shared locale resources
├── ops/
│   └── openclaw/openclaw.json  # OpenClaw gateway configuration (JSON5, read-only mount)
├── docker-compose.yml          # openclaw + seren-api + seren-web stack
├── docs/                       # Reference documentation (17 chapters)
└── tools/                      # One-off scripts (commit-msg hook, …)
```

## Contributing

- Install the root git hooks once: `npm install` at the repo root activates `simple-git-hooks`. Pre-commit runs `dotnet format --verify-no-changes` on the server solution and `pnpm -C src/ui lint` + `pnpm -C src/ui typecheck` on the UI workspace.
- Commit messages follow Conventional Commits; the allowed types (`feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `build`, `ci`, `perf`, `style`, `revert`) are enforced by `tools/scripts/check-conventional-commit.js` via the `commit-msg` hook.
- Code analysis is on with `TreatWarningsAsErrors=true` for the server; avoid `NoWarn` suppressions unless you can justify them inline.
- Design principles: SOLID, DRY, KISS, enterprise-grade. Comments explain *why*, never *what*.

## License

MIT.

## Background

Seren's architecture is inspired by the open-source [AIRI](https://github.com/moeru-ai/airi) project, re-implemented against a different stack (C# hub + OpenClaw backend). The reference documentation for the architectural choices lives under [`./docs/00-sommaire.md`](./docs/00-sommaire.md) as a 17-chapter French series, starting with an overview and descending into protocols, packages, and the reproduction guide.
