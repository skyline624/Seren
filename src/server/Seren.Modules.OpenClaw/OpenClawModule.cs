using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Seren.Application.Chat;
using Seren.Application.Modules;
using Seren.Infrastructure.OpenClaw;
using Seren.Infrastructure.OpenClaw.Gateway;
using Seren.Infrastructure.OpenClaw.Identity;
using Seren.Infrastructure.Realtime;

namespace Seren.Modules.OpenClaw;

/// <summary>
/// OpenClaw module: every adapter that talks to the external OpenClaw
/// gateway — persistent paired WebSocket, chat streaming pipeline, model
/// catalog client, history reader, session-key provider, Ed25519 device
/// identity, options + validators.
/// </summary>
/// <remarks>
/// <para>
/// This is the largest module by surface and intentionally so: extracting
/// it from <c>InfrastructureServiceCollectionExtensions</c> is the
/// scalability proof of the <see cref="ISerenModule"/> contract. Roughly
/// seventy lines of registration that previously lived in the host.
/// </para>
/// <para>
/// Configuration sections kept under their legacy names (<c>OpenClaw</c>,
/// <c>OpenClaw:Chat</c>, <c>OpenClaw:Resilience</c>) — the module reads
/// them as-is. Migrating to the <c>Modules:OpenClaw:*</c> convention is a
/// follow-up because env-var renames affect every deployment.
/// </para>
/// </remarks>
public sealed class OpenClawModule : ISerenModule
{
    /// <inheritdoc />
    public string Id => "openclaw";

    /// <inheritdoc />
    public string Version =>
        typeof(OpenClawModule).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(OpenClawModule).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <inheritdoc />
    public void Configure(ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var configuration = context.Configuration;

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

        // Stable session-key provider so chat / voice handlers stay decoupled
        // from OpenClawOptions (which lives in Infrastructure).
        services.AddSingleton<IChatSessionKeyProvider, OpenClawChatSessionKeyProvider>();
    }
}
