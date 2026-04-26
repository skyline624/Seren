using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Seren.Application.Characters.Import;
using Seren.Application.Characters.Personas;
using Seren.Application.Modules;
using Seren.Infrastructure.Characters;
using Seren.Infrastructure.Persistence.Json;

namespace Seren.Modules.Characters;

/// <summary>
/// Characters module: persona persistence (JSON store + avatar files),
/// Character Card v3 import pipeline, persona workspace synchronisation
/// (writes <c>IDENTITY.md</c> / <c>SOUL.md</c> on activation, captures the
/// reverse on demand), and the <c>/api/characters/*</c> REST surface.
/// </summary>
/// <remarks>
/// <para>
/// Configuration section: <c>Modules:Characters</c>. For one release a
/// fallback to the legacy <c>CharacterStore</c> root section is honored so
/// existing deployments don't break.
/// </para>
/// <para>
/// Implementation classes still physically reside in
/// <c>Seren.Infrastructure/Characters</c> and
/// <c>Seren.Infrastructure/Persistence/Json</c> during Phase 2 — the source
/// split is deferred until the contract has been exercised on more modules.
/// </para>
/// </remarks>
public sealed class CharactersModule : ISerenModule, IEndpointMappingModule
{
    /// <inheritdoc />
    public string Id => "characters";

    /// <inheritdoc />
    public string Version =>
        typeof(CharactersModule).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(CharactersModule).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <inheritdoc />
    public void Configure(ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var section = ResolveSection(context);

        context.Services
            .AddOptions<CharacterStoreOptions>()
            .Bind(section)
            .ValidateOnStart();

        context.Services.AddSingleton<IValidator<CharacterStoreOptions>, CharacterStoreOptionsValidator>();
        context.Services.AddSingleton<ICharacterRepository, JsonCharacterRepository>();

        // Character Card v3 import pipeline.
        context.Services.AddSingleton<ICharacterAvatarStore, FileSystemCharacterAvatarStore>();
        context.Services.AddSingleton<ICharacterCardParser, CharacterCardV3Parser>();
        context.Services.AddSingleton<ICharacterImportMetrics, OtelCharacterImportMetrics>();

        // Persona workspace writer + reader. The OpenTelemetry meter
        // `seren.persona` covers both via OtelPersonaMetrics (writes +
        // captures), surfaced through two narrower abstractions
        // (IPersonaWriterMetrics, IPersonaCaptureMetrics).
        context.Services.AddSingleton<OtelPersonaMetrics>();
        context.Services.AddSingleton<IPersonaWriterMetrics>(sp => sp.GetRequiredService<OtelPersonaMetrics>());
        context.Services.AddSingleton<IPersonaCaptureMetrics>(sp => sp.GetRequiredService<OtelPersonaMetrics>());
        context.Services.AddSingleton<IPersonaWorkspaceWriter, FileSystemPersonaWorkspaceWriter>();
        context.Services.AddSingleton<IPersonaWorkspaceReader, FileSystemPersonaWorkspaceReader>();
        context.Services.AddHostedService<PersonaWorkspaceSynchronizer>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapCharacterEndpoints();
    }

    /// <summary>
    /// Resolves the configuration section to bind. Prefers the new
    /// <c>Modules:Characters</c> path; falls back to the legacy
    /// <c>CharacterStore</c> root section so existing appsettings continue
    /// to work during the migration.
    /// </summary>
    private static IConfigurationSection ResolveSection(ModuleContext context)
    {
        var modern = context.Configuration.GetSection(context.SectionName);
        if (modern.Exists())
        {
            return modern;
        }

        return context.Configuration.GetSection(CharacterStoreOptions.SectionName);
    }
}
