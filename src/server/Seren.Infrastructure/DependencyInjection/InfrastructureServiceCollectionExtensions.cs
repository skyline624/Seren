using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Domain.Abstractions;
using Seren.Infrastructure.Authentication;
using Seren.Infrastructure.Cors;
using Seren.Infrastructure.OpenClaw;
using Seren.Infrastructure.OpenClaw.Gateway;
using Seren.Infrastructure.OpenClaw.Identity;
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

        // Persistence (JSON character store + avatar files) and the
        // Character Card v3 import pipeline + persona workspace
        // synchroniser moved to Seren.Modules.Characters. The host
        // registers them via builder.Services.AddSerenModules(typeof(CharactersModule))
        // in Program.cs.

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

        // Typed HttpClient for POST /tools/invoke (used for the gateway tool's
        // `restart` and `config.patch` actions — model pinning, catalog refresh).
        // We resolve via IHttpClientFactory so DNS + handler pooling stay
        // consistent with the rest of Seren's outbound HTTP.
        services.AddHttpClient<IOpenClawClient, OpenClawGatewayModelsClient>();

        // In-memory cache used by ModelEndpoints to serve `/api/models`
        // without hitting OpenClaw's models.list RPC on every request.
        services.AddMemoryCache();

        // Persisted-transcript reader + session reset (chat.history /
        // sessions.reset upstream RPCs).
        services.AddSingleton<IOpenClawHistory, OpenClawGatewayHistoryClient>();

        // Chat attachments (validator + PDF/plain-text extractors) moved to
        // Seren.Modules.ChatAttachments. The host registers it via
        // builder.Services.AddSerenModules(typeof(ChatAttachmentsModule))
        // in Program.cs.

        // Stable session-key provider so chat / voice handlers stay decoupled
        // from OpenClawOptions (which lives in Infrastructure).
        services.AddSingleton<IChatSessionKeyProvider, OpenClawChatSessionKeyProvider>();

        // Audio providers (STT/TTS) moved to Seren.Modules.Audio. The host
        // registers them via builder.Services.AddSerenModules(typeof(AudioModule))
        // in Program.cs. Provider implementations still physically reside in
        // Seren.Infrastructure/Audio during Phase 1.

        // ── Rate limiting ──────────────────────────────────────────────────
        services.AddSerenRateLimiting(configuration);

        // ── CORS ───────────────────────────────────────────────────────────
        services.AddSerenCors(configuration);

        // ── Security headers (CSP, HSTS, X-Frame-Options, etc.) ────────────
        services.AddSerenSecurityHeaders(configuration);

        return services;
    }
}
