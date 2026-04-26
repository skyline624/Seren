using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Seren.Application.Modules;
using Seren.Infrastructure.Audio;
using Shouldly;
using Xunit;

namespace Seren.Modules.Audio.Tests;

/// <summary>
/// Unit tests for the <see cref="AudioModule"/> contract: validates that
/// <see cref="ISerenModule.Configure"/> wires the right STT/TTS providers
/// based on whether an OpenAI API key is configured. No
/// <c>WebApplicationFactory</c> here — modules are testable in isolation
/// via a plain <see cref="ServiceCollection"/>.
/// </summary>
public sealed class AudioModuleTests
{
    [Fact]
    public void Configure_WithApiKeyUnderModernSection_ResolvesOpenAiProviders()
    {
        var services = BuildServices(new()
        {
            ["Modules:Audio:OpenAiApiKey"] = "sk-test",
        });

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISttProvider>().ShouldBeOfType<OpenAiSttProvider>();
        provider.GetRequiredService<ITtsProvider>().ShouldBeOfType<OpenAiTtsProvider>();
    }

    [Fact]
    public void Configure_WithoutApiKey_FallsBackToNoOpProviders()
    {
        var services = BuildServices(new());

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISttProvider>().ShouldBeOfType<NoOpSttProvider>();
        provider.GetRequiredService<ITtsProvider>().ShouldBeOfType<NoOpTtsProvider>();
    }

    [Fact]
    public void Configure_WithApiKeyUnderLegacySection_StillBindsProviders()
    {
        // Compat path: deployments that still have a top-level "Audio:" section
        // in appsettings continue to work during the migration window.
        var services = BuildServices(new()
        {
            ["Audio:OpenAiApiKey"] = "sk-test-legacy",
        });

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISttProvider>().ShouldBeOfType<OpenAiSttProvider>();
        provider.GetRequiredService<ITtsProvider>().ShouldBeOfType<OpenAiTtsProvider>();
    }

    [Fact]
    public void Module_Identity_IsStable()
    {
        var module = new AudioModule();
        module.Id.ShouldBe("audio");
        module.Version.ShouldNotBeNullOrWhiteSpace();
    }

    private static ServiceCollection BuildServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();

        // The module needs IOptions<T> machinery. AddOptions() is idempotent
        // and is normally invoked transitively via AddOptions<T>() inside the
        // module — listing it explicitly here keeps the test self-contained.
        services.AddLogging();

        services.AddSerenModules(configuration, typeof(AudioModule));

        return services;
    }
}
