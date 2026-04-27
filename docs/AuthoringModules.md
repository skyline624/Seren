# Authoring a Seren module — cookbook

This guide walks through adding a new module to Seren end-to-end, with a hypothetical `Weather` module as the running example. Read [`docs/09-plugins.md`](09-plugins.md) first for the architectural rationale.

A complete module typically has:

- A **server assembly** (`Seren.Modules.Weather`) that registers options + services + endpoints.
- An optional **UI workspace package** (`@seren/module-weather`) that contributes a settings tab + chat hooks + locales.
- An entry in `appsettings.json` (or env vars) under `Modules:Weather`.
- A reference in the composition root (`Program.cs` server-side, `main.ts` UI-side).

You can ship server-only or UI-only — the two halves are independent.

---

## 1. Server module (.NET 10)

### 1.1 Create the project

```bash
mkdir -p src/server/Seren.Modules.Weather
cat > src/server/Seren.Modules.Weather/Seren.Modules.Weather.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Seren.Modules.Weather</RootNamespace>
    <AssemblyName>Seren.Modules.Weather</AssemblyName>
    <Description>Seren module — weather lookup capability.</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Seren.Application\Seren.Application.csproj" />
  </ItemGroup>
</Project>
EOF

docker run --rm -v $PWD:/repo -w /repo mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet sln src/server/Seren.sln add src/server/Seren.Modules.Weather/Seren.Modules.Weather.csproj
```

### 1.2 Module class

```csharp
// src/server/Seren.Modules.Weather/WeatherModule.cs
using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Modules;

namespace Seren.Modules.Weather;

/// <summary>
/// Weather module: registers an HTTP-backed weather provider.
/// </summary>
public sealed class WeatherModule : ISerenModule
{
    public string Id => "weather";

    public string Version =>
        typeof(WeatherModule).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";

    public void Configure(ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Services
            .AddOptions<WeatherOptions>()
            .Bind(context.Configuration.GetSection(context.SectionName))
            .ValidateOnStart();

        context.Services.AddSingleton<IValidator<WeatherOptions>, WeatherOptionsValidator>();
        context.Services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>();
    }
}

public sealed class WeatherOptions
{
    /// <summary>OpenMeteo-compatible API base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.open-meteo.com/v1";
}

public sealed class WeatherOptionsValidator : AbstractValidator<WeatherOptions>
{
    public WeatherOptionsValidator()
    {
        RuleFor(o => o.BaseUrl).NotEmpty();
    }
}

public interface IWeatherProvider
{
    Task<WeatherSnapshot> GetCurrentAsync(double lat, double lon, CancellationToken ct);
}

public sealed record WeatherSnapshot(double TemperatureC, string Condition);
```

The provider implementation (`OpenMeteoWeatherProvider`) uses the typed `HttpClient` injected by `AddHttpClient<>` and the bound `IOptions<WeatherOptions>`. Same pattern as every existing module.

### 1.3 (Optional) Endpoints

If the module exposes REST endpoints, implement `IEndpointMappingModule`:

```csharp
public sealed class WeatherModule : ISerenModule, IEndpointMappingModule
{
    // ...

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/weather/now", async (
            IWeatherProvider provider,
            double lat,
            double lon,
            CancellationToken ct) =>
        {
            var snapshot = await provider.GetCurrentAsync(lat, lon, ct);
            return Results.Ok(snapshot);
        }).WithName("GetCurrentWeather").WithTags("weather");
    }
}
```

### 1.4 (Optional) WebSocket broadcast

To push updates to UI peers, declare an `IModuleBroadcast` notification + a leaf handler that `Mediator` will discover:

```csharp
public sealed record WeatherUpdatedNotification(WeatherSnapshot Payload) : IModuleBroadcast
{
    string IModuleBroadcast.EventType => "weather:updated";
    object IModuleBroadcast.Payload => Payload;
    PeerId? IModuleBroadcast.ExcludingPeer => null;
}

public sealed class WeatherUpdatedBroadcastHandler
    : ModuleBroadcastHandler<WeatherUpdatedNotification>
{
    public WeatherUpdatedBroadcastHandler(
        ISerenHub hub,
        ILogger<WeatherUpdatedBroadcastHandler> logger)
        : base(hub, logger) { }
}
```

Anywhere in the module: `await mediator.Publish(new WeatherUpdatedNotification(snapshot), ct);` — every UI peer receives a `weather:updated` envelope.

### 1.5 (Optional) Inbound WebSocket frames

To accept frames from peers (e.g. `weather:request:lookup`):

```csharp
public sealed class WeatherInboundHandler : IInboundEnvelopeHandler
{
    public string TypePrefix => "weather:";
    public bool DetachFromReceiveLoop => false;

    public async Task HandleAsync(
        PeerId peerId, WebSocketEnvelope envelope, CancellationToken ct)
    {
        // ... deserialise, validate, dispatch ...
    }
}
```

Register in `Configure`:

```csharp
context.Services.AddSingleton<IInboundEnvelopeHandler, WeatherInboundHandler>();
```

The host's `SerenWebSocketSessionProcessor` will route `weather:*` frames to your handler after authentication.

### 1.6 Wire the module into the host

`src/server/Seren.Server.Api/Seren.Server.Api.csproj`:

```xml
<ProjectReference Include="..\Seren.Modules.Weather\Seren.Modules.Weather.csproj" />
```

`src/server/Seren.Server.Api/Dockerfile` — add the csproj to the restore-cache COPY list.

`src/server/Seren.Server.Api/Program.cs`:

```csharp
using Seren.Modules.Weather;
// ...
builder.Services.AddSerenModules(
    builder.Configuration,
    typeof(AudioModule),
    typeof(CharactersModule),
    typeof(ChatAttachmentsModule),
    typeof(OpenClawModule),
    typeof(WeatherModule));
```

### 1.7 Tests

Create `src/server/tests/Seren.Modules.Weather.Tests/`:

```csharp
public sealed class WeatherModuleTests
{
    [Fact]
    public void Configure_RegistersProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Weather:BaseUrl"] = "https://api.example.com",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerenModules(configuration, typeof(WeatherModule));

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IWeatherProvider>().ShouldBeOfType<OpenMeteoWeatherProvider>();
    }
}
```

Pure unit test — no `WebApplicationFactory` needed.

---

## 2. UI module (Vue 3)

### 2.1 Create the workspace package

```bash
mkdir -p src/ui/packages/seren-module-weather/src
cat > src/ui/packages/seren-module-weather/package.json <<'EOF'
{
  "name": "@seren/module-weather",
  "version": "0.1.0",
  "type": "module",
  "main": "./src/index.ts",
  "module": "./src/index.ts",
  "types": "./src/index.ts",
  "exports": {
    ".": {
      "types": "./src/index.ts",
      "import": "./src/index.ts"
    }
  },
  "files": ["src"],
  "scripts": {
    "typecheck": "vue-tsc --noEmit",
    "lint": "eslint .",
    "test": "vitest run"
  },
  "dependencies": {
    "@seren/sdk": "workspace:*",
    "@seren/ui-shared": "workspace:*",
    "vue": "catalog:",
    "vue-i18n": "catalog:",
    "pinia": "catalog:"
  },
  "devDependencies": {
    "vue-tsc": "catalog:",
    "typescript": "catalog:",
    "vitest": "catalog:"
  }
}
EOF
```

A `tsconfig.json` extending `../../tsconfig.base.json` (mirror `seren-module-audio/tsconfig.json`).

### 2.2 Module entry point

```typescript
// src/ui/packages/seren-module-weather/src/index.ts
import { defineSerenModule, type SerenModuleDefinition } from '@seren/sdk'

const ICON_WEATHER = `<svg viewBox="0 0 24 24"><path d="..."/></svg>`

const weatherModule: SerenModuleDefinition = defineSerenModule({
  id: 'weather',
  version: '0.1.0',
  settings: {
    labelKey: 'modules.weather.title',
    icon: ICON_WEATHER,
    component: () => import('./SettingsTab.vue'),
    order: 60,
  },
  locales: {
    en: { weather: { title: 'Weather', city: 'Default city' } },
    fr: { weather: { title: 'Météo', city: 'Ville par défaut' } },
  },
  install({ chatHooks }) {
    return chatHooks.register('weather', {
      onBeforeSend: async ({ text }) => {
        if (text.toLowerCase().includes('weather')) {
          // pre-fetch a snapshot, attach a hint, ...
        }
      },
    })
  },
})

export default weatherModule
```

### 2.3 Settings tab component

```vue
<!-- src/ui/packages/seren-module-weather/src/SettingsTab.vue -->
<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { ref } from 'vue'

const { t } = useI18n()
const city = ref('')
</script>

<template>
  <section class="settings-section">
    <h3 class="settings-section__title">{{ t('modules.weather.title') }}</h3>
    <div class="settings-field">
      <label class="settings-field__label" for="weather-city">
        {{ t('modules.weather.city') }}
      </label>
      <input id="weather-city" v-model="city" type="text">
    </div>
  </section>

  <!-- styles: copy section-common.css inline or import from @seren/ui-shared -->
</template>
```

### 2.4 Wire into the app

`src/ui/apps/seren-web/package.json`:

```json
"@seren/module-weather": "workspace:*"
```

`src/ui/apps/seren-web/src/main.ts`:

```typescript
import weatherModule from '@seren/module-weather'

app.use(serenModulesPlugin, {
  modules: [audioModule, weatherModule],
})
```

### 2.5 i18n

If your module ships its own locale strings, list them in `locales` (declarative) AND duplicate them under `modules.{id}.*` in `src/ui/packages/seren-i18n/src/locales/{en,fr}.json` until a runtime locale-merge utility lands. This double-bookkeeping is documented as a known short-term limitation.

---

## 3. Build, test, ship

### 3.1 Server

```bash
docker run --rm -v $PWD:/repo -w /repo mcr.microsoft.com/dotnet/sdk:10.0 \
  sh -c "dotnet build src/server/Seren.sln -p:SkipTypeGen=true -p:NuGetAudit=false && \
         dotnet test src/server/Seren.sln -p:SkipTypeGen=true -p:NuGetAudit=false \
           --filter 'FullyQualifiedName~WeatherModule'"
```

### 3.2 UI

```bash
docker run --rm -v $PWD:/repo -w /repo/src/ui node:22 \
  sh -c "corepack enable && pnpm install --no-frozen-lockfile && \
         pnpm -F @seren/sdk build && pnpm -F @seren/ui-shared build && \
         pnpm -F @seren/module-weather typecheck && pnpm -F @seren/web typecheck"
```

The SDK and ui-shared rebuilds matter only if you've **modified** them — adding a new module is non-invasive, but downstream packages read from `dist/index.mjs` so any contract change must propagate.

### 3.3 Live E2E

```bash
docker compose build seren-api
docker compose up -d --force-recreate seren-api
curl -s -o /dev/null -w 'HTTP %{http_code}\n' http://localhost:5080/api/weather/now?lat=48.85&lon=2.35
```

Open `http://localhost:9080`, navigate to Settings — your tab should show up in the registered order.

---

## 4. Migrating an existing feature folder into a module

Phases 1-4 of the module rollout extracted four feature folders (`Audio`, `Characters`, `ChatAttachments`, `OpenClaw`). The pattern proven across these:

1. **Don't move source files immediately.** Create the new `Seren.Modules.X` assembly that references both `Seren.Application` (for abstractions) and `Seren.Infrastructure` (for current impl). The module's `Configure(ModuleContext)` registers the same DI bindings the host used to do directly.
2. **Lift the DI bloc.** Cut the `// ── X ──` section from `InfrastructureServiceCollectionExtensions.cs` and paste it (with light context renaming) into `XModule.Configure`. Replace `services.X` with `context.Services.X`. The `using` imports often need pruning afterwards.
3. **Add the `appsettings` fallback.** Keep the legacy section name (`Audio`, `CharacterStore`, …) reachable via a `ResolveSection(context)` helper for one release. New deployments use `Modules:X`; existing deployments don't break.
4. **Wire `AddSerenModules(...)` and `app.MapSerenModules()`.** Drop the corresponding `app.MapXEndpoints()` if the module is `IEndpointMappingModule`.
5. **Run the full test suite.** Integration tests (especially the 42 WebSocket ones) act as the non-regression backbone — if they're green, the module wiring is functionally equivalent.
6. **Don't migrate handler logic in the same commit.** If a module exposes WebSocket relay handlers, keep them as-is in the first pass. A follow-up commit can convert them to `ModuleBroadcastHandler<T>` subclasses once the module's contract is stable.

The same approach applies to a UI section: extract into a workspace package, expose via `defineSerenModule`, drop the hardcoded section from `SettingsPanel.vue`. Existing Pinia stores can stay where they are (in `@seren/ui-shared`) — moving them rarely pays off.

---

## 5. FAQ

**Q: My module needs scoped services.**
A: `context.Services.AddScoped<...>()` works; the module is itself a singleton (registered once at startup) but that doesn't constrain the lifetime of the services it registers.

**Q: Can two modules expose the same setting tab id?**
A: No — the registry uses `id` as a unique key (last write wins). If you must duplicate a tab (different deployment variants), namespace the id (`audio.advanced`, `audio.experimental`).

**Q: My module is platform-specific (mobile only).**
A: Each app (`seren-web`, `seren-mobile`, `seren-desktop`) registers its own list of modules in its `main.ts`. A mobile-only module simply isn't imported by `seren-web`.

**Q: Hot-reload?**
A: Out of scope. Restart the dev server.

**Q: Permissions / sandbox?**
A: Out of scope. Modules are in-tree code, audited at compile time. If you ever ship third-party out-of-tree modules, look at .NET `AssemblyLoadContext` — the contract is dimensioned to support it but the host work is not done.
