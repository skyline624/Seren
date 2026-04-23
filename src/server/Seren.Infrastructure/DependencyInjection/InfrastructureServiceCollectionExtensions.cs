using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Characters.Import;
using Seren.Application.Characters.Personas;
using Seren.Application.Chat;
using Seren.Domain.Abstractions;
using Seren.Infrastructure.Characters;
using Seren.Infrastructure.Audio;
using Seren.Infrastructure.Authentication;
using Seren.Infrastructure.Cors;
using Seren.Infrastructure.OpenClaw;
using Seren.Infrastructure.OpenClaw.Gateway;
using Seren.Infrastructure.OpenClaw.Identity;
using Seren.Infrastructure.Persistence.Json;
using Seren.Infrastructure.RateLimiting;
using Seren.Infrastructure.Realtime;
using Seren.Infrastructure.Security;

namespace Seren.Infrastructure.DependencyInjection;

/// <summary>
/// DI extensions for the <c>Seren.Infrastructure</c> layer.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers infrastructure services: flat-file character store, peer
    /// registry, connection registry, WebSocket hub, session processor,
    /// OpenClaw adapter, audio providers, authentication, and binds
    /// options.
    /// </summary>
    public static IServiceCollection AddSerenInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Persistence (JSON file for characters) ────────────────────────
        // OpenClaw owns chat transcripts; Seren only needs to persist the
        // small set of user-defined characters (avatar preset + voice +
        // agent id). A single JSON file on a mounted volume is simpler
        // than EF + SQLite for this use case.
        services
            .AddOptions<CharacterStoreOptions>()
            .Bind(configuration.GetSection(CharacterStoreOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidator<CharacterStoreOptions>, CharacterStoreOptionsValidator>();
        services.AddSingleton<Application.Abstractions.ICharacterRepository, JsonCharacterRepository>();

        // Character Card v3 import pipeline.
        services.AddSingleton<ICharacterAvatarStore, FileSystemCharacterAvatarStore>();
        services.AddSingleton<ICharacterCardParser, CharacterCardV3Parser>();
        services.AddSingleton<ICharacterImportMetrics, OtelCharacterImportMetrics>();

        // Persona workspace writer + reader — refreshes OpenClaw's
        // IDENTITY.md + SOUL.md on character activation and captures
        // the reverse (workspace → new Character) on demand. Single
        // OpenTelemetry meter `seren.persona` covers both via
        // OtelPersonaMetrics (writes + captures).
        services.AddSingleton<OtelPersonaMetrics>();
        services.AddSingleton<IPersonaWriterMetrics>(sp => sp.GetRequiredService<OtelPersonaMetrics>());
        services.AddSingleton<IPersonaCaptureMetrics>(sp => sp.GetRequiredService<OtelPersonaMetrics>());
        services.AddSingleton<IPersonaWorkspaceWriter, FileSystemPersonaWorkspaceWriter>();
        services.AddSingleton<IPersonaWorkspaceReader, FileSystemPersonaWorkspaceReader>();
        services.AddHostedService<PersonaWorkspaceSynchronizer>();

        // ── Authentication ────────────────────────────────────────────────
        services
            .AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<ITokenService, JwtTokenService>();

        // Token revocation store + background sweeper.
        services.AddSingleton<InMemoryTokenRevocationStore>();
        services.AddSingleton<ITokenRevocationStore>(sp =>
            sp.GetRequiredService<InMemoryTokenRevocationStore>());
        services.AddHostedService<TokenRevocationSweeper>();

        // ── WebSocket hub ──────────────────────────────────────────────────
        services
            .AddOptions<SerenHubOptions>()
            .Bind(configuration.GetSection(SerenHubOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IPeerRegistry, InMemoryPeerRegistry>();
        services.AddSingleton<IReadOnlyPeerRegistry>(sp =>
            sp.GetRequiredService<IPeerRegistry>());

        services.AddSingleton<IWebSocketConnectionRegistry, WebSocketConnectionRegistry>();
        services.AddSingleton<ISerenHub, SerenWebSocketHub>();

        services.AddScoped<SerenWebSocketSessionProcessor>();
        services.AddHostedService<StaleSessionSweeper>();

        // ── OpenClaw adapter ───────────────────────────────────────────────
        services
            .AddOptions<OpenClawOptions>()
            .Bind(configuration.GetSection(OpenClawOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidator<OpenClawOptions>, OpenClawOptionsValidator>();
        services.AddSingleton<OpenClawTokenValidator>();

        // Persistent Ed25519 device identity — generated on first boot, used
        // at every handshake so OpenClaw keeps our self-declared scopes.
        services.AddSingleton<IDeviceIdentityStore, FileSystemDeviceIdentityStore>();

        // Persistent gateway WebSocket — the single transport to OpenClaw.
        services.AddSingleton<OpenClawWebSocketClient>();
        services.AddHostedService(sp => sp.GetRequiredService<OpenClawWebSocketClient>());
        services.AddSingleton<IOpenClawGateway>(sp =>
            sp.GetRequiredService<OpenClawWebSocketClient>());

        // Chat event fan-out: one dispatcher feeding per-runId subscriptions.
        // The router (OpenClawEventRouter) is auto-registered by Mediator's
        // source generator since it implements INotificationHandler<>.
        services.AddSingleton<OpenClawChatStreamDispatcher>();

        services.AddSingleton<IOpenClawChat, OpenClawGatewayChatClient>();

        // Idle / total chat-stream timeouts. Bound from OpenClaw:Chat so
        // env vars look like OpenClaw__Chat__IdleTimeout=00:00:30. The
        // total timeout is also forwarded as chat.send.timeoutMs so
        // OpenClaw enforces the cap if Seren ever loses contact.
        services
            .AddOptions<ChatStreamOptions>()
            .Bind(configuration.GetSection(ChatStreamOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidator<ChatStreamOptions>, ChatStreamOptionsValidator>();

        // Resilience policy: retry-on-idle and fallback model cascade.
        services
            .AddOptions<ChatResilienceOptions>()
            .Bind(configuration.GetSection(ChatResilienceOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidator<ChatResilienceOptions>, ChatResilienceOptionsValidator>();

        // Tracks the active runId per session so user-Stop events and the
        // server-side timeout safety net can target the right run.
        services.AddSingleton<IChatRunRegistry, InMemoryChatRunRegistry>();

        // OTel metrics wrapper + the shared streaming pipeline itself.
        // Singleton lifetime: the pipeline is stateless (all state flows
        // through ChatStreamRequest) and a singleton avoids re-creating
        // the Meter instruments per request.
        services.AddSingleton<ChatStreamMetrics>();
        services.AddSingleton<IChatStreamPipeline, ChatStreamPipeline>();

        // Typed HttpClient for POST /tools/invoke (used to SIGUSR1-restart
        // the gateway and refresh its Ollama catalog on demand). We resolve
        // it via IHttpClientFactory so DNS + handler pooling stay consistent
        // with the rest of Seren's outbound HTTP.
        services.AddHttpClient<IOpenClawClient, OpenClawGatewayModelsClient>();

        // Direct file writer for pinning agents.defaults.model.primary in
        // openclaw.json. Exists because OpenClaw's config.patch / sessions.patch
        // RPCs require operator.admin scope (which we don't hold); see the
        // scope comment on `OpenClawGatewayProtocol.BackendOperatorScopes`.
        services.AddSingleton<IOpenClawConfigWriter, OpenClawJsonConfigWriter>();

        // In-memory cache used by ModelEndpoints to serve `/api/models`
        // without hitting OpenClaw's models.list RPC on every request.
        services.AddMemoryCache();

        // Persisted-transcript reader + session reset (chat.history /
        // sessions.reset upstream RPCs).
        services.AddSingleton<IOpenClawHistory, OpenClawGatewayHistoryClient>();

        // Stable session-key provider so chat / voice handlers stay decoupled
        // from OpenClawOptions (which lives in Infrastructure).
        services.AddSingleton<IChatSessionKeyProvider, OpenClawChatSessionKeyProvider>();

        // ── Audio (STT/TTS) ────────────────────────────────────────────────
        services
            .AddOptions<AudioOptions>()
            .Bind(configuration.GetSection(AudioOptions.SectionName))
            .ValidateOnStart();

        var audioConfig = configuration.GetSection(AudioOptions.SectionName);
        var apiKey = audioConfig["OpenAiApiKey"];
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);

        if (hasApiKey)
        {
            services.AddHttpClient<OpenAiSttProvider>();
            services.AddSingleton<ISttProvider>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAiSttProvider));
                var options = sp.GetRequiredService<IOptions<AudioOptions>>().Value;
                return new OpenAiSttProvider(httpClient, options);
            });

            services.AddHttpClient<OpenAiTtsProvider>();
            services.AddSingleton<ITtsProvider>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAiTtsProvider));
                var options = sp.GetRequiredService<IOptions<AudioOptions>>().Value;
                return new OpenAiTtsProvider(httpClient, options);
            });
        }
        else
        {
            services.AddSingleton<ISttProvider, NoOpSttProvider>();
            services.AddSingleton<ITtsProvider, NoOpTtsProvider>();
        }

        // ── Rate limiting ──────────────────────────────────────────────────
        services.AddSerenRateLimiting(configuration);

        // ── CORS ───────────────────────────────────────────────────────────
        services.AddSerenCors(configuration);

        // ── Security headers (CSP, HSTS, X-Frame-Options, etc.) ────────────
        services.AddSerenSecurityHeaders(configuration);

        return services;
    }
}
