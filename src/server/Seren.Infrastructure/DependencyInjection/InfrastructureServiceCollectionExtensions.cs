using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Domain.Abstractions;
using Seren.Infrastructure.Audio;
using Seren.Infrastructure.Authentication;
using Seren.Infrastructure.Cors;
using Seren.Infrastructure.OpenClaw;
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
            .Bind(configuration.GetSection(CharacterStoreOptions.SectionName));

        services.AddSingleton<Application.Abstractions.ICharacterRepository, JsonCharacterRepository>();

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

        // ── Chat (marker contract, default prompt) ────────────────────────
        services
            .AddOptions<ChatOptions>()
            .Bind(configuration.GetSection(ChatOptions.SectionName));

        // ── OpenClaw adapter ───────────────────────────────────────────────
        services
            .AddOptions<OpenClawOptions>()
            .Bind(configuration.GetSection(OpenClawOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidator<OpenClawOptions>, OpenClawOptionsValidator>();
        services.AddSingleton<OpenClawTokenValidator>();

        var openClawBaseUrl = configuration.GetSection(OpenClawOptions.SectionName)["BaseUrl"]
            ?? "http://127.0.0.1:18789";
        services.AddHttpClient<OpenClawRestClient>(client =>
            {
                client.BaseAddress = new Uri(openClawBaseUrl);
            })
            .AddOpenClawResilience();

        services.AddSingleton<IOpenClawClient>(sp =>
            sp.GetRequiredService<OpenClawRestClient>());

        services.AddSingleton<OpenClawWebSocketClient>();
        services.AddHostedService(sp => sp.GetRequiredService<OpenClawWebSocketClient>());

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
