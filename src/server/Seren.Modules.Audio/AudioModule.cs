using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Modules;
using Seren.Infrastructure.Audio;

namespace Seren.Modules.Audio;

/// <summary>
/// Audio module: registers the speech-to-text and text-to-speech providers.
/// Picks the OpenAI-compatible provider when an API key is configured,
/// otherwise falls back to no-op providers so the host stays functional in
/// development environments without external dependencies.
/// </summary>
/// <remarks>
/// <para>
/// Configuration section: <c>Modules:Audio</c>. For one release a fallback
/// to the legacy <c>Audio</c> section is honored so existing deployments
/// don't break — to be removed once operators have migrated.
/// </para>
/// <para>
/// The module follows the Seren contract from
/// <see cref="Seren.Application.Modules.ISerenModule"/>: a single
/// <see cref="ISerenModule.Configure"/> call wires every required service.
/// Providers are still physically located in <c>Seren.Infrastructure.Audio</c>
/// during Phase 1 — the source split is deferred until the contract has
/// stabilised across other modules.
/// </para>
/// </remarks>
public sealed class AudioModule : ISerenModule
{
    /// <inheritdoc />
    public string Id => "audio";

    /// <inheritdoc />
    public string Version =>
        typeof(AudioModule).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(AudioModule).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <inheritdoc />
    public void Configure(ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var section = ResolveSection(context);

        context.Services
            .AddOptions<AudioOptions>()
            .Bind(section)
            .ValidateOnStart();

        var apiKey = section["OpenAiApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            context.Services.AddHttpClient<OpenAiSttProvider>();
            context.Services.TryAddSingleton<ISttProvider>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                    .CreateClient(nameof(OpenAiSttProvider));
                var options = sp.GetRequiredService<IOptions<AudioOptions>>().Value;
                return new OpenAiSttProvider(httpClient, options);
            });

            context.Services.AddHttpClient<OpenAiTtsProvider>();
            context.Services.TryAddSingleton<ITtsProvider>(sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>()
                    .CreateClient(nameof(OpenAiTtsProvider));
                var options = sp.GetRequiredService<IOptions<AudioOptions>>().Value;
                return new OpenAiTtsProvider(httpClient, options);
            });
        }
        else
        {
            context.Services.TryAddSingleton<ISttProvider, NoOpSttProvider>();
            context.Services.TryAddSingleton<ITtsProvider, NoOpTtsProvider>();
        }

        // No-op speaker recognizer: kicks in when the VoxMind module is
        // absent or its speaker subsystem is disabled. Registered with
        // TryAdd so VoxMindModule can Replace() with its sherpa-onnx
        // adapter when the optional dependency is present.
        context.Services.TryAddSingleton<ISpeakerRecognizer, NoOpSpeakerRecognizer>();
    }

    /// <summary>
    /// Resolves the configuration section to bind. Prefers the new
    /// <c>Modules:Audio</c> path; falls back to the legacy <c>Audio</c> root
    /// so existing appsettings continue to work during the migration.
    /// </summary>
    private static IConfigurationSection ResolveSection(ModuleContext context)
    {
        var modern = context.Configuration.GetSection(context.SectionName);
        if (modern.Exists())
        {
            return modern;
        }

        var legacy = context.Configuration.GetSection(AudioOptions.SectionName);
        return legacy;
    }
}
