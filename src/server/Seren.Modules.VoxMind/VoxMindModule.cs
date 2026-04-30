using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seren.Application.Abstractions;
using Seren.Application.Modules;
using Seren.Modules.VoxMind.Configuration;
using Seren.Modules.VoxMind.Diagnostics;
using Seren.Modules.VoxMind.Endpoints;
using Seren.Modules.VoxMind.F5Tts;
using Seren.Modules.VoxMind.Models;
using Seren.Modules.VoxMind.Speakers;
using Seren.Modules.VoxMind.Speakers.Database;
using Seren.Modules.VoxMind.Transcription;
using Seren.Modules.VoxMind.Transcription.Engines;
using Seren.Modules.VoxMind.Tts;

namespace Seren.Modules.VoxMind;

/// <summary>
/// VoxMind module — wires the local STT engines (Parakeet + Whisper) and
/// the F5-TTS synthesiser into Seren via the standard
/// <see cref="ISttProvider"/> / <see cref="ITtsProvider"/> abstractions,
/// registers the post-hoc language detector, the metrics surface, the
/// FluentValidation options adapter, and (via
/// <see cref="IHealthCheckProviderModule"/>) the per-bundle health probes.
/// </summary>
/// <remarks>
/// <para>
/// Configuration section: <c>Modules:voxmind</c>. When
/// <see cref="VoxMindOptions.Enabled"/> is <c>false</c>, the module is a
/// no-op and the host falls back to <c>AudioModule</c>'s NoOp / OpenAI
/// providers.
/// </para>
/// <para>
/// The module uses <see cref="ServiceCollectionDescriptorExtensions.Replace"/>
/// to take precedence over the prior provider registrations from
/// AudioModule — order in <c>AddSerenModules(...)</c> is irrelevant for
/// resolution.
/// </para>
/// </remarks>
public sealed class VoxMindModule : ISerenModule, IHealthCheckProviderModule, IEndpointMappingModule
{
    /// <inheritdoc />
    public string Id => "voxmind";

    /// <inheritdoc />
    public string Version =>
        typeof(VoxMindModule).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(VoxMindModule).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <inheritdoc />
    public void Configure(ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var section = context.Configuration.GetSection(context.SectionName);

        context.Services
            .AddOptions<VoxMindOptions>()
            .Bind(section)
            .ValidateOnStart();

        // FluentValidation → IValidateOptions adapter → fail-fast on bad config.
        context.Services.TryAddSingleton<IValidator<VoxMindOptions>, VoxMindOptionsValidator>();
        context.Services.AddSingleton<IValidateOptions<VoxMindOptions>, FluentValidationVoxMindOptionsValidator>();

        // Backward-compat for legacy single-engine config — maps
        // Stt:ModelDir → Stt:Parakeet:ModelDir at boot.
        context.Services.AddSingleton<IPostConfigureOptions<VoxMindOptions>, VoxMindOptionsBackwardCompat>();

        // When the module is disabled the providers and detector are not
        // wired, so AudioModule's TryAdd registrations stay in place.
        if (!section.GetValue("Enabled", defaultValue: true))
        {
            return;
        }

        context.Services.TryAddSingleton<ILanguageDetector, StopwordLanguageDetector>();
        context.Services.TryAddSingleton<VoxMindMetrics>();

        // Model manager (catalog + on-disk presence + HTTPS download).
        // The named HttpClient is reused per-stream and has no per-request
        // timeout — bundles can take minutes on slow links.
        context.Services
            .AddHttpClient("voxmind-downloads", c =>
            {
                c.Timeout = Timeout.InfiniteTimeSpan;
                c.DefaultRequestHeaders.UserAgent.ParseAdd($"Seren-VoxMind/{Version}");
            });
        context.Services.TryAddSingleton<IModelStorage, ModelStorage>();
        context.Services.TryAddSingleton<IModelDownloadService, ModelDownloadService>();

        // Register every local STT engine into the collection. Adding a new
        // engine is one extra TryAddEnumerable line — the router picks them
        // up automatically via the IEnumerable<IVoxMindSttEngine> dep.
        context.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IVoxMindSttEngine, ParakeetSttEngine>());
        context.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IVoxMindSttEngine, WhisperSttEngine>());

        context.Services.Replace(
            ServiceDescriptor.Singleton<ISttProvider>(sp =>
                new VoxMindSttProvider(
                    sp.GetRequiredService<IOptions<VoxMindOptions>>(),
                    sp.GetRequiredService<ILogger<VoxMindSttProvider>>(),
                    sp.GetRequiredService<VoxMindMetrics>(),
                    sp.GetServices<IVoxMindSttEngine>())));

        context.Services.Replace(
            ServiceDescriptor.Singleton<ITtsProvider>(sp =>
                new VoxMindTtsProvider(
                    sp.GetRequiredService<IOptions<VoxMindOptions>>(),
                    sp.GetRequiredService<ILogger<VoxMindTtsProvider>>(),
                    sp.GetRequiredService<ILogger<F5LanguageEngine>>(),
                    sp.GetRequiredService<VoxMindMetrics>())));

        // Speaker recognition wiring. The DbContext is registered as a
        // factory because the service is a Singleton and creates short-
        // lived contexts on demand (matches the upstream VoxMind pattern).
        // Auto-migration runs once per boot — the SQLite file lives on
        // the `voxmind_speakers` Docker volume so the schema travels with
        // the embeddings.
        context.Services.AddDbContextFactory<VoxMindSpeakerDbContext>((sp, builder) =>
        {
            var opts = sp.GetRequiredService<IOptions<VoxMindOptions>>().Value;
            var dbPath = string.IsNullOrWhiteSpace(opts.Speakers.DbPath)
                ? "/data/voxmind/speakers/speakers.db"
                : opts.Speakers.DbPath;
            var dbDirectory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
            }
            builder.UseSqlite($"Data Source={dbPath}");
        });

        context.Services.TryAddSingleton<ISpeakerEmbeddingExtractor>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<VoxMindOptions>>().Value.Speakers;
            return new SherpaOnnxSpeakerEmbeddingExtractor(
                opts.ModelPath,
                opts.NumThreads,
                sp.GetRequiredService<ILogger<SherpaOnnxSpeakerEmbeddingExtractor>>());
        });

        context.Services.TryAddSingleton<ISpeakerIdentificationService, SherpaOnnxSpeakerService>();

        // Replace the no-op recognizer registered by AudioModule with
        // the VoxMind adapter so SubmitVoiceInputHandler /
        // TranscribeVoiceHandler can resolve a single
        // ISpeakerRecognizer dependency without conditional wiring.
        context.Services.Replace(
            ServiceDescriptor.Singleton<ISpeakerRecognizer, VoxMindSpeakerRecognizer>());

        // Hosted service: apply pending EF Core migrations on boot, then
        // step out (the speaker service warms its embedding cache lazily
        // on the first identification call).
        context.Services.AddHostedService<SpeakerDbMigrationHostedService>();
    }

    /// <inheritdoc />
    public void RegisterHealthChecks(IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddCheck<ParakeetHealthCheck>(ParakeetHealthCheck.Name);
        builder.AddCheck<WhisperHealthCheck>(WhisperHealthCheck.Name);
        builder.AddCheck<F5TtsHealthCheck>(F5TtsHealthCheck.Name);
        builder.AddCheck<SpeakerIdHealthCheck>(SpeakerIdHealthCheck.Name);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapVoxMindModelEndpoints();
    }
}
