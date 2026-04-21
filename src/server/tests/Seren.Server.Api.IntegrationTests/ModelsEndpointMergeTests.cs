using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Abstractions;
using Shouldly;
using Xunit;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// Exercises the merge behaviour of <c>GET /api/models</c>: combines the
/// OpenClaw cloud catalog with locally-installed Ollama models, de-dups
/// by id, orders alphabetically, and caches the result.
/// </summary>
public sealed class ModelsEndpointMergeTests : IClassFixture<ModelsEndpointMergeTests.StubCatalogFactory>
{
    private readonly StubCatalogFactory _factory;

    public ModelsEndpointMergeTests(StubCatalogFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_ReturnsUnionOfOpenClawAndOllamaSourcesSortedById()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Reset(
            openClaw: [new ModelInfo("anthropic/claude-opus", "Claude Opus")],
            ollama: [new ModelInfo("ollama/seren-qwen:latest", "qwen 9B Q8_0")]);

        using var client = _factory.CreateClient();
        var models = await client.GetFromJsonAsync<ModelInfo[]>("/api/models", ct);

        models.ShouldNotBeNull();
        models.Length.ShouldBe(2);
        models[0].Id.ShouldBe("anthropic/claude-opus");
        models[1].Id.ShouldBe("ollama/seren-qwen:latest");
    }

    [Fact]
    public async Task GetAll_OnDuplicateIds_KeepsOneEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Reset(
            openClaw: [new ModelInfo("shared/id", "From OpenClaw")],
            ollama: [new ModelInfo("shared/id", "From Ollama")]);

        using var client = _factory.CreateClient();
        var models = await client.GetFromJsonAsync<ModelInfo[]>("/api/models", ct);

        models.ShouldNotBeNull();
        models.Length.ShouldBe(1);
        models[0].Id.ShouldBe("shared/id");
        // OpenClaw wins (first in the concat sequence) — deterministic order matters.
        models[0].Description.ShouldBe("From OpenClaw");
    }

    [Fact]
    public async Task GetAll_CachesResponseWithinShortWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Reset(
            openClaw: [new ModelInfo("cached/a", null)],
            ollama: []);

        using var client = _factory.CreateClient();
        _ = await client.GetFromJsonAsync<ModelInfo[]>("/api/models", ct);

        // Mutate the stubs' backing lists directly (without calling Reset,
        // which would also clear the cache). The second call must serve
        // the cached payload instead of re-querying the stubs.
        _factory.MutateOpenClawModels([new ModelInfo("cached/b", null)]);

        var second = await client.GetFromJsonAsync<ModelInfo[]>("/api/models", ct);

        second.ShouldNotBeNull();
        second.Length.ShouldBe(1);
        second[0].Id.ShouldBe("cached/a");
    }

    public sealed class StubCatalogFactory : SerenTestFactory
    {
        private readonly StubOpenClaw _openClaw = new();
        private readonly StubOllama _ollama = new();

        public void Reset(IReadOnlyList<ModelInfo> openClaw, IReadOnlyList<ModelInfo> ollama)
        {
            _openClaw.Models = openClaw;
            _ollama.Models = ollama;

            // Drop the memo cache so each test sees the stubs it just set,
            // not a payload cached by a prior test. The endpoint's TTL of
            // 60 s would otherwise leak across the whole class.
            using var scope = Services.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            cache.Remove("models:merged");
        }

        /// <summary>
        /// Mutates the OpenClaw stub's model list without touching the
        /// endpoint cache. Used by the cache-hit test to prove that the
        /// second call serves the cached payload rather than re-querying.
        /// </summary>
        public void MutateOpenClawModels(IReadOnlyList<ModelInfo> models)
        {
            _openClaw.Models = models;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                ReplaceSingleton(services, typeof(IOpenClawClient), _openClaw);
                // IOllamaClient is registered via AddHttpClient — swap it for
                // our pure stub so we don't need a real HTTP mock here.
                ReplaceService(services, typeof(IOllamaClient), _ollama);
            });
        }

        private static void ReplaceSingleton(IServiceCollection services, Type serviceType, object instance)
        {
            var existing = services.Where(d => d.ServiceType == serviceType).ToList();
            foreach (var d in existing)
            {
                services.Remove(d);
            }
            services.AddSingleton(serviceType, instance);
        }

        private static void ReplaceService(IServiceCollection services, Type serviceType, object instance)
        {
            ReplaceSingleton(services, serviceType, instance);
        }

        private sealed class StubOpenClaw : IOpenClawClient
        {
            public IReadOnlyList<ModelInfo> Models { get; set; } = Array.Empty<ModelInfo>();
            public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
                => Task.FromResult(Models);
        }

        private sealed class StubOllama : IOllamaClient
        {
            public IReadOnlyList<ModelInfo> Models { get; set; } = Array.Empty<ModelInfo>();
            public Task<IReadOnlyList<ModelInfo>> GetLocalModelsAsync(CancellationToken ct = default)
                => Task.FromResult(Models);
        }
    }
}
