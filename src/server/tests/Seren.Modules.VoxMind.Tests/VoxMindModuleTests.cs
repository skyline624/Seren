using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Seren.Application.Modules;
using Seren.Modules.VoxMind.Transcription;
using Seren.Modules.VoxMind.Tts;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests;

public sealed class VoxMindModuleTests
{
    [Fact]
    public void Configure_Registers_SttProvider()
    {
        var services = BuildServices([]);

        var provider = services.BuildServiceProvider();
        var stt = provider.GetService<ISttProvider>();

        stt.ShouldNotBeNull();
        stt.ShouldBeOfType<VoxMindSttProvider>();
    }

    [Fact]
    public void Configure_Registers_TtsProvider()
    {
        var services = BuildServices([]);

        var provider = services.BuildServiceProvider();
        var tts = provider.GetService<ITtsProvider>();

        tts.ShouldNotBeNull();
        tts.ShouldBeOfType<VoxMindTtsProvider>();
    }

    [Fact]
    public void Configure_WhenDisabled_DoesNotRegisterProviders()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["Modules:voxmind:Enabled"] = "false",
        });

        var provider = services.BuildServiceProvider();
        provider.GetService<ISttProvider>().ShouldBeNull();
        provider.GetService<ITtsProvider>().ShouldBeNull();
    }

    [Fact]
    public async Task SttProvider_WithoutModelDir_ReturnsEmptyResult()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["Modules:voxmind:Stt:ModelDir"] = string.Empty,
        });

        var stt = services.BuildServiceProvider().GetRequiredService<ISttProvider>();
        var result = await stt.TranscribeAsync([0, 0, 0, 0], "wav", TestContext.Current.CancellationToken);

        result.Text.ShouldBe(string.Empty);
        result.Language.ShouldBe("fr");
        result.Confidence.ShouldBe(0f);
    }

    [Fact]
    public async Task TtsProvider_WithoutCheckpoints_YieldsNoChunks()
    {
        var services = BuildServices([]);

        var tts = services.BuildServiceProvider().GetRequiredService<ITtsProvider>();
        var chunks = new List<TtsChunk>();
        var ct = TestContext.Current.CancellationToken;
        await foreach (var chunk in tts.SynthesizeAsync("hello", language: "fr", ct: ct).WithCancellation(ct))
        {
            chunks.Add(chunk);
        }

        chunks.ShouldBeEmpty();
    }

    [Fact]
    public void Module_Id_Is_Voxmind()
    {
        var module = new VoxMindModule();
        module.Id.ShouldBe("voxmind");
    }

    [Fact]
    public void Module_Version_Is_Not_Empty()
    {
        var module = new VoxMindModule();
        module.Version.ShouldNotBeNullOrWhiteSpace();
    }

    private static ServiceCollection BuildServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSerenModules(configuration, typeof(VoxMindModule));
        return services;
    }
}
