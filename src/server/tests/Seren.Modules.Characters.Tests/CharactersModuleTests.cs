using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Seren.Application.Characters.Import;
using Seren.Application.Characters.Personas;
using Seren.Application.Modules;
using Seren.Infrastructure.Persistence.Json;
using Shouldly;
using Xunit;

namespace Seren.Modules.Characters.Tests;

/// <summary>
/// Unit tests for the <see cref="CharactersModule"/> contract: validates
/// that <see cref="ISerenModule.Configure"/> wires the persistence,
/// import-pipeline and persona-workspace services into DI. The module is
/// exercised via a plain <see cref="ServiceCollection"/> — no
/// <c>WebApplicationFactory</c> needed.
/// </summary>
public sealed class CharactersModuleTests
{
    [Fact]
    public void Configure_RegistersPersistenceServices()
    {
        var services = BuildServices(new()
        {
            // CharacterStore options need a writable path; use /tmp for the test —
            // services.BuildServiceProvider() doesn't actually open the file.
            ["Modules:Characters:StorePath"] = "/tmp/seren-test-characters.json",
        });

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICharacterRepository>().ShouldBeOfType<JsonCharacterRepository>();
        provider.GetRequiredService<ICharacterAvatarStore>().ShouldNotBeNull();
        provider.GetRequiredService<ICharacterCardParser>().ShouldNotBeNull();
        provider.GetRequiredService<ICharacterImportMetrics>().ShouldNotBeNull();
        provider.GetRequiredService<IValidator<CharacterStoreOptions>>().ShouldNotBeNull();
    }

    [Fact]
    public void Configure_RegistersPersonaWorkspaceServices()
    {
        var services = BuildServices(new()
        {
            ["Modules:Characters:StorePath"] = "/tmp/seren-test-characters.json",
        });

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPersonaWriterMetrics>().ShouldNotBeNull();
        provider.GetRequiredService<IPersonaCaptureMetrics>().ShouldNotBeNull();
        provider.GetRequiredService<IPersonaWorkspaceWriter>().ShouldNotBeNull();
        provider.GetRequiredService<IPersonaWorkspaceReader>().ShouldNotBeNull();
    }

    [Fact]
    public void Module_Identity_IsStable()
    {
        var module = new CharactersModule();
        module.Id.ShouldBe("characters");
        module.Version.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Configure_LegacySectionFallback_IsHonored()
    {
        // Compat path: deployments with a top-level "CharacterStore:" section
        // (the previous convention) keep working.
        var services = BuildServices(new()
        {
            ["CharacterStore:StorePath"] = "/tmp/seren-test-legacy.json",
        });

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICharacterRepository>().ShouldNotBeNull();
    }

    private static ServiceCollection BuildServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerenModules(configuration, typeof(CharactersModule));

        return services;
    }
}
