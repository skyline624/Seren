# Project AIRI — Documentation Technique Exhaustive

> **Version du document** : 1.0 — 2026-04-15
> **Version du projet analysée** : 0.9.0-rc.1
> **Langue** : Français
> **Objectif** : Fournir une documentation suffisamment complète pour permettre la reproduction intégrale du projet à partir de ce seul document.

---

## Table des matières

| # | Document | Contenu |
|---|----------|---------|
| 00 | [Sommaire](00-sommaire.md) | Ce document — navigation générale |
| 01 | [Vue d'ensemble du projet](01-vue-ensemble.md) | Présentation, objectifs, philosophie, public cible |
| 02 | [Architecture globale](02-architecture-globale.md) | Schémas d'architecture, topologie, flux de données |
| 03 | [Structure du monorepo](03-structure-monorepo.md) | Organisation des dossiers, outils, pnpm workspaces, Turbo |
| 04 | [Applications (apps/)](04-applications.md) | stage-web, stage-tamagotchi, stage-pocket, server, ui-server-auth |
| 05 | [Packages UI](05-packages-ui.md) | stage-ui, ui, stage-ui-three, stage-ui-live2d, stage-shared, i18n |
| 06 | [Packages serveur](06-packages-serveur.md) | server-runtime, server-sdk, server-shared, plugin-protocol |
| 07 | [Packages infrastructure](07-packages-infrastructure.md) | audio, pipelines, electron-*, stream-kit, drivers, WarpDrive |
| 08 | [Services d'intégration](08-services.md) | discord-bot, minecraft, telegram-bot, satori-bot, twitter-services |
| 09 | [Plugins AIRI](09-plugins.md) | bilibili, claude-code, homeassistant, llm-orchestrator, web-extension |
| 10 | [Protocole WebSocket & Eventa](10-protocoles.md) | Enveloppes, types d'événements, handshake, heartbeat, routage |
| 11 | [Diagrammes UML & schémas](11-diagrammes-uml.md) | Diagrammes de classes, séquence, déploiement, état |
| 12 | [Build, outillage et CI](12-build-outillage.md) | Turbo, tsdown, electron-vite, Vite, ESLint, tests |
| 13 | [Tests et qualité](13-tests-qualite.md) | Vitest, couverture, stratégies de test, exemples |
| 14 | [Guide de reproduction](14-guide-reproduction.md) | Reproduire le projet étape par étape |
| 15 | [Exemples de code](15-exemples-code.md) | Exemples concrets pour chaque couche |
| 16 | [Annexes](16-annexes.md) | Glossaire, références, FAQ, dépannage |

---

## Comment utiliser cette documentation

Chaque document est **autonome** et peut être consulté indépendamment, mais l'ordre proposé facilite la compréhension progressive :

1. **Si vous découvrez le projet** : lisez 01 → 02 → 03 → 04 dans l'ordre
2. **Si vous développez un plugin** : focalisez sur 06 → 09 → 10 → 15
3. **Si vous intégrez une nouvelle plateforme** : lisez 02 → 06 → 08 → 10
4. **Si vous voulez reproduire le projet depuis zéro** : suivez 14 en consultant 01 à 13 au besoin

---

## Conventions typographiques

- **Chemins de fichiers** : `apps/stage-tamagotchi/src/main/index.ts`
- **Commandes shell** : formatées en blocs ``` ```bash ``` ```
- **Noms d'événements AIRI** : `module:authenticate`, `input:text`
- **Noms de modules npm internes** : `@proj-airi/server-sdk`
- **Noms de classes / types** : `Client`, `WebSocketEvent`
- **Notes** : `// NOTICE:`, `// TODO:`, `// REVIEW:` (conventions utilisées dans le code)

---

## Résumé en une phrase

**Project AIRI est un monorepo pnpm qui implémente un « être cyber vivant » modulaire : une IA incarnée (VRM/Live2D) capable de converser, jouer à des jeux, et interagir sur plusieurs plateformes, articulée autour d'un *server-runtime* WebSocket et de multiples surfaces frontales (desktop Electron, web PWA, mobile Capacitor).**
