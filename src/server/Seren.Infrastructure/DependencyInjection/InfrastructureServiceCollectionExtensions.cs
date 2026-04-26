using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Seren.Domain.Abstractions;
using Seren.Infrastructure.Authentication;
using Seren.Infrastructure.Cors;
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

        // OpenClaw adapter (every gateway-facing service: persistent paired
        // WebSocket, chat-stream pipeline, model catalog client, history
        // reader, session-key provider, device identity, options + validators)
        // moved to Seren.Modules.OpenClaw. The host registers it via
        // builder.Services.AddSerenModules(typeof(OpenClawModule)) in
        // Program.cs.

        // Chat attachments (validator + PDF/plain-text extractors) moved to
        // Seren.Modules.ChatAttachments. The host registers it via
        // builder.Services.AddSerenModules(typeof(ChatAttachmentsModule))
        // in Program.cs.

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
