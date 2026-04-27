# 09 — Modules Seren

Seren expose une architecture **modulaire** inspirée du système de plugins d'AIRI, mais radicalement simplifiée pour la stack Seren (.NET 10 monolithique + Vue 3 PWA). Là où AIRI s'appuie sur un plugin-host Electron avec hot-reload, sandbox de capabilities et IPC `eventa`, Seren se contente des primitives idiomatiques de sa propre stack : extension `IServiceCollection` côté serveur, plugin Vue + store Pinia côté UI. Pas de hot-reload, pas de sandbox, pas de scan de manifest au runtime — registration explicite, type-safe, source-generated.

Ce chapitre décrit l'architecture telle qu'elle est implémentée dans Seren ; pour une analyse comparative du système AIRI d'origine, voir [`docs/06-packages-serveur.md`](06-packages-serveur.md) (plugin-protocol AIRI).

## 9.1 Philosophie de design

| Principe | Concrétisation Seren |
|----------|---------------------|
| KISS | Modules in-tree, registration explicite via `params Type[]` dans `Program.cs` (et `app.use(serenModulesPlugin, { modules: [...] })` côté UI) |
| ISP (SOLID) | Le contrat de base (`ISerenModule`) est minimal. Les capacités optionnelles (`IEndpointMappingModule`, `IHealthCheckProviderModule`, `IInboundEnvelopeHandler`, `IModuleBroadcast`) sont des interfaces séparées que les modules implémentent **à la demande** |
| DRY | `Mediator` source generator + `FluentValidation` assembly scan + Pinia store registry — réutilise les mécanismes déjà en place, n'invente pas de framework parallèle |
| OCP (Open/Closed) | `EventTypes.cs` reste un catalogue closed (le core), les modules ajoutent leurs constants tagués `[ModuleEvent]` dans leur propre assembly (additive convention) |
| DIP | Les abstractions cross-module (`ITtsProvider`, `ICharacterRepository`, `IOpenClawClient`, …) restent dans `Seren.Application/Abstractions`. Les modules fournissent les implémentations |

## 9.2 Côté serveur (.NET 10)

### 9.2.1 Contrat de base

```csharp
namespace Seren.Application.Modules;

public interface ISerenModule
{
    string Id { get; }            // kebab-case ; détermine la section appsettings "Modules:{Id}"
    string Version { get; }       // SemVer (généralement AssemblyInformationalVersion)
    void Configure(ModuleContext context);
}

public sealed record ModuleContext(
    IServiceCollection Services,
    IConfiguration Configuration,
    string SectionName);          // "Modules:{ModuleId}"
```

Un module qui ne fait que registrer des services (providers, options, validators) n'implémente que `ISerenModule`. Le `Configure(ModuleContext)` est appelé une fois au composition root, avant `app.Build()`.

### 9.2.2 Capacités opt-in

```csharp
public interface IEndpointMappingModule
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

public interface IHealthCheckProviderModule
{
    void RegisterHealthChecks(IHealthChecksBuilder builder);
}

public interface IInboundEnvelopeHandler
{
    string TypePrefix { get; }    // ex: "weather:"
    bool DetachFromReceiveLoop { get; }
    Task HandleAsync(PeerId peerId, WebSocketEnvelope envelope, CancellationToken ct);
}

public interface IModuleBroadcast : INotification
{
    string EventType { get; }
    object Payload { get; }
    PeerId? ExcludingPeer { get; }
}
```

Chaque interface est cumulable : `CharactersModule` implémente `ISerenModule + IEndpointMappingModule`, `OpenClawModule` n'implémente que `ISerenModule` (ses endpoints sont mappés ailleurs). La structure permet d'ajouter de futures capacités (par exemple `IMigrationModule` pour des migrations DB) sans toucher les modules existants.

### 9.2.3 Composition root

```csharp
// Program.cs
builder.Services.AddSerenApplication();
builder.Services.AddSerenInfrastructure(builder.Configuration);
builder.Services.AddSerenModules(
    builder.Configuration,
    typeof(AudioModule),
    typeof(CharactersModule),
    typeof(ChatAttachmentsModule),
    typeof(OpenClawModule));

// après app.Build()
app.MapSerenModules();   // appelle MapEndpoints sur chaque IEndpointMappingModule
```

`AddSerenModules` instancie chaque type via son constructeur sans paramètre, l'enregistre comme singleton (typé concret + interface `ISerenModule`), puis appelle `Configure(ModuleContext)`. Une exception explicite est levée si un type ne réalise pas `ISerenModule` ou n'a pas de constructeur paramless — le composition root échoue rapidement plutôt que de cacher une mauvaise registration.

### 9.2.4 Modules livrés

| Assembly | Contenu | Capacités |
|----------|---------|-----------|
| `Seren.Modules.Audio` | STT/TTS providers (OpenAI + NoOp fallback), `AudioOptions` | `ISerenModule` |
| `Seren.Modules.Characters` | Persistence JSON + avatar store + Character Card v3 import + persona workspace synchroniser | `ISerenModule + IEndpointMappingModule` (`/api/characters/*`) |
| `Seren.Modules.ChatAttachments` | Validator + extracteurs PDF / plain text + DTO de constraints | `ISerenModule + IEndpointMappingModule` (`/api/chat/attachments/*`) |
| `Seren.Modules.OpenClaw` | Adaptateur complet : WebSocket persistant, chat-stream pipeline, model catalog, history, session-key, device identity | `ISerenModule` |

Les implémentations concrètes (classes `OpenAi*Provider`, `JsonCharacterRepository`, `OpenClawWebSocketClient`, etc.) résident encore dans `Seren.Infrastructure/{Audio,Characters,Persistence,OpenClaw}/` lors des phases 1-4 — le module en assure le wiring DI sans déplacer les fichiers source. Une refacto ultérieure peut splitter chaque module dans son propre assembly de bout en bout, mais ce n'est pas un prérequis du contrat.

### 9.2.5 WebSocket events module-spécifiques

Le `SerenWebSocketSessionProcessor` traite huit types d'events core via un switch hard-codé (`transport:heartbeat`, `module:authenticate`, `module:announce`, `input:chat:*`, `input:text`, `input:voice`). Les frames qui ne matchent aucun de ces cas sont relayées au premier `IInboundEnvelopeHandler` enregistré dont `TypePrefix` correspond — l'authentification reste appliquée AVANT le dispatch (le gate vit dans le processor, pas dans les handlers).

Pour émettre des events sortants, un module publie une `INotification` qui implémente `IModuleBroadcast`. Le handler générique `ModuleBroadcastHandler<T>` (à étendre dans une sous-classe close pour que le source generator Mediator l'enregistre) traduit la notification en `WebSocketEnvelope` et appelle `ISerenHub.BroadcastAsync`. Aucune modification du `SerenWebSocketSessionProcessor` nécessaire.

L'attribut `[ModuleEvent(moduleId, direction)]` peut être posé sur les constants string déclarant ces nouveaux types — purement informatif pour la doc et le futur TypeGen, n'influence pas le runtime. `Seren.Contracts.Events.EventTypes` reste le catalogue core inchangé (additive convention).

### 9.2.6 Configuration appsettings

Convention : `Modules:{Id}` est la section dédiée à un module. Le `ModuleContext.SectionName` la pré-calcule pour que le module évite la duplication. Les modules existants conservent un fallback vers leur ancienne section root (`Audio`, `CharacterStore`) pendant une release — voir le code de chaque module pour la stratégie exacte.

```json5
{
  "Modules": {
    "Audio": {
      "OpenAiApiKey": "${OPENAI_API_KEY}"
    },
    "Characters": {
      "StorePath": "/data/characters.json"
    }
  }
}
```

## 9.3 Côté UI (Vue 3 + pnpm workspace)

### 9.3.1 Contrat TypeScript

```typescript
// @seren/sdk
export interface SerenModuleDefinition {
  readonly id: string             // Aligné sur le ISerenModule.Id côté serveur quand un module est paired
  readonly version: string

  settings?: {
    labelKey: string              // clé i18n
    icon: string                  // SVG inline (rendu via v-html)
    component: () => Promise<{ default: unknown }>  // lazy import
    order?: number                // tri du nav settings (default 100)
    badgeKey?: string             // ex: "settings.nav.comingSoonBadge"
  }

  locales?: { en: Record<string, unknown>, fr: Record<string, unknown> }

  install?: (context: SerenModuleContext) => void | (() => void)
}
```

Le helper `defineSerenModule(definition)` retourne l'objet inchangé — c'est uniquement un type guard pour bénéficier de l'IntelliSense au point d'écriture du module.

### 9.3.2 Registry + plugin Vue

`useSerenModulesRegistry` (Pinia) collectionne les modules enregistrés. `serenModulesPlugin` fait le bootstrap : pour chaque module passé en option, il l'enregistre dans le registry et appelle son `install()` avec un `SerenModuleContext` partagé (qui expose la `ChatHookRegistry`).

```typescript
// seren-web/src/main.ts
import { serenModulesPlugin } from '@seren/ui-shared'
import audioModule from '@seren/module-audio'

app.use(pinia)
app.use(i18n)
app.use(serenModulesPlugin, { modules: [audioModule] })
```

### 9.3.3 Chat hooks

```typescript
export interface ChatHooks {
  onBeforeSend?: (msg: { text: string, sessionId: string }) => Promise<void> | void
  onAfterSend?: (msg: { text: string, sessionId: string }) => void
  onTokenLiteral?: (token: string) => void
  onAssistantResponseEnd?: (final: { text: string }) => void
}
```

Quatre hooks volontairement minimaux. Un module les déclare via son `install({ chatHooks })` :

```typescript
install({ chatHooks }) {
  return chatHooks.register('my-module', {
    onAfterSend: ({ text }) => console.debug('user said:', text),
  })
}
```

Le retour de `register` est une fonction de cleanup — utile pour les tests, optionnel en prod (les modules vivent jusqu'à la fermeture de l'app).

### 9.3.4 SettingsPanel

Le composant `SettingsPanel.vue` fusionne au render time les sections core hardcoded (appearance, connection, llm, character/coming-soon) avec les `settingsTabs` exposés par le registry, triés par `order`. Le rendu utilise `defineAsyncComponent(tab.component)` pour la lazy-load — le bundle initial reste léger, chaque tab module n'embarque que sa section quand l'utilisateur clique dessus.

### 9.3.5 Module pilote : `@seren/module-audio`

Workspace package autonome (`src/ui/packages/seren-module-audio/`). Fournit l'onglet Voice (seuil VAD) en réutilisant le store `useVoiceSettingsStore` de `@seren/ui-shared` — la persistence localStorage des utilisateurs existants est préservée. Les locales sont déclarées dans le `locales` du module (forme statique, à terme mergées au runtime ; pour Phase 4 elles ont aussi été ajoutées en dur au catalogue `seren-i18n` sous le namespace `modules.audio.*`).

## 9.4 Différences avec AIRI

| AIRI | Seren |
|------|-------|
| Plugin-host Electron avec lifecycle géré | Registration explicite dans `Program.cs` / `main.ts` |
| Hot-reload via `plugin-host` | Pas de hot-reload (redéploiement complet) |
| Sandbox `permissions.requested ∩ granted` | Modules in-tree, audités à la compilation |
| Manifest JSON `manifest.plugin.airi.moeru.ai/v1` | Code C# / TS — `ISerenModule` + `defineSerenModule()` |
| Eventa pattern-based EventBus | Mediator `INotification` + `IModuleBroadcast` |
| File-system scan `plugins/` | `params Type[]` côté serveur, `modules: [...]` côté UI |
| Plugin SDK séparé du server SDK | Application + UI SDK uniformes (`ISerenModule` côté C#, `defineSerenModule` côté TS) |

## 9.5 Hors scope explicite

Délibérément non implémentés à ce stade :

1. **Hot-reload de modules** — Seren se redéploie ; n'est pas un IDE.
2. **Sandbox / permissions runtime** — modules in-tree audités à la compilation, pas de modèle de privilège.
3. **`AssemblyLoadContext` out-of-tree** — `ISerenModule.Configure(ModuleContext)` est dimensionné pour le supporter mais non implémenté. Exit-ramp documenté.
4. **Module federation Vite** — modules linkés au build de chaque app (`seren-web`, `seren-mobile`, `seren-desktop`), pas chargés à runtime.
5. **LLM tools / skills** — appartiennent à OpenClaw, pas à Seren (cf. memory `project_openclaw_config_mutations`).
6. **TypeGen automatique des module events** — abstractions (`[ModuleEvent]`, `IModuleBroadcast`) en place, target MSBuild à ajouter quand un module aura ses propres event types à exposer.

## 9.6 Aller plus loin

- Cookbook pas-à-pas pour créer un nouveau module → [`AuthoringModules.md`](AuthoringModules.md).
- Architecture serveur globale → [`02-architecture-globale.md`](02-architecture-globale.md).
- Composition root + DI conventions → [`06-packages-serveur.md`](06-packages-serveur.md).
- WebSocket protocol → [`10-protocoles.md`](10-protocoles.md).
