using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Seren.Application.Modules;
using Shouldly;
using Xunit;

namespace Seren.Modules.VoxMind.Tests.Tts;

/// <summary>
/// WarmUp tests focus on the contract surface: the call must complete without
/// throwing even when the requested language has no checkpoint on disk
/// (it should be a graceful no-op). End-to-end timing tests that exercise
/// the LRU cache against real ONNX checkpoints live in the smoke-test suite
/// (gated behind RequiresModels).
/// </summary>
public sealed class WarmUpAsyncTests
{
    [Fact]
    public async Task WarmUp_NoCheckpointOnDisk_CompletesSilently()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["Modules:voxmind:Tts:Languages:fr:PreprocessModelPath"] = "/no/such/file.onnx",
            ["Modules:voxmind:Tts:Languages:fr:TransformerModelPath"] = "/no/such/file.onnx",
            ["Modules:voxmind:Tts:Languages:fr:DecodeModelPath"] = "/no/such/file.onnx",
            ["Modules:voxmind:Tts:Languages:fr:TokensPath"] = "/no/such/file.txt",
        });

        var tts = services.BuildServiceProvider().GetRequiredService<ITtsProvider>();

        await Should.NotThrowAsync(() => tts.WarmUpAsync("fr", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WarmUp_UnknownLanguage_FallsBackToDefaultAndCompletesSilently()
    {
        var services = BuildServices(new Dictionary<string, string?> { });
        var tts = services.BuildServiceProvider().GetRequiredService<ITtsProvider>();

        // "xx" is not configured → ResolveLanguage falls back to options.DefaultLanguage,
        // which is also not configured → no-op.
        await Should.NotThrowAsync(() => tts.WarmUpAsync("xx", TestContext.Current.CancellationToken));
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
