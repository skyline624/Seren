# Seren

> Hub de coordination temps-réel pour un personnage IA incarné (avatar VRM/Live2D, chat vocal multi-surface), propulsé par [OpenClaw](https://github.com/openclaw/openclaw).

Seren est une refonte architecturale du projet open-source [AIRI](https://github.com/moeru-ai/airi) avec les choix techniques suivants :

- **Serveur** : C# / .NET 10 LTS (ASP.NET Core, Clean Architecture, Mediator, EF Core, OpenTelemetry)
- **Frontend** : Vue 3 (PWA), Tauri 2 (desktop), Capacitor 8 (mobile), Three.js + three-vrm, PIXI + Live2D
- **IA backend** : [OpenClaw Gateway](https://github.com/openclaw/openclaw) pour LLM, mémoire, skills, canaux de messagerie
- **Normes** : SOLID, DRY, KISS, Enterprise-Grade

La documentation détaillée (inspirée d'AIRI) se trouve dans `docs/`.
Le plan de développement actuel est dans `C:\Users\pc\.claude\plans\transient-hatching-swing.md`.

## Prérequis

- **.NET 10 SDK** (LTS, `10.0.100+`) — https://dotnet.microsoft.com/download/dotnet/10.0
- **Node.js 22+ LTS** — https://nodejs.org/
- **pnpm 10+** — `corepack enable && corepack prepare pnpm@latest --activate`
- **OpenClaw Gateway** — `npm install -g openclaw && openclaw onboard`
- Pour desktop : **Rust** (Tauri 2) — https://rustup.rs/
- Pour mobile : **Xcode** (iOS) / **Android Studio** (Android)

## Structure

```
Seren/
├── docs/                     # Documentation de référence (AIRI analysée)
├── src/
│   ├── server/               # Backend C# (solution .NET)
│   └── ui/                   # Frontend TypeScript (workspace pnpm)
└── .github/workflows/        # CI/CD
```

## Développement local

```bash
# Installer les dépendances serveur
rtk dotnet restore src/server/Seren.sln

# Installer les dépendances UI
cd src/ui && rtk pnpm install

# Lancer le hub Seren (terminal 1)
rtk dotnet run --project src/server/Seren.Server.Api

# Lancer OpenClaw Gateway (terminal 2, depuis une install OpenClaw)
openclaw gateway

# Lancer l'app web (terminal 3)
rtk pnpm -F seren-web dev
```

## Tests

```bash
# Tests serveur
rtk dotnet test src/server/Seren.sln

# Tests UI
cd src/ui && rtk pnpm test
```

## Licence

MIT
