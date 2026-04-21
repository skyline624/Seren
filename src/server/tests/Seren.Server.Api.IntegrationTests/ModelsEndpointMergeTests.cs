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
/// Exercises <c>GET /api/models</c>: OpenClaw is the sole source, the
/// response is ordered alphabetically by id, and consecutive calls within
/// the cache window reuse the first payload instead of re-querying the
/// gateway.
/// </summary>
public sealed class ModelsEndpointMergeTests : IClassFixture<ModelsEndpointMergeTests.StubCatalogFactory>
{
    private readonly StubCatalogFactory _factory;

    public ModelsEndpointMergeTests(StubCatalogFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_ReturnsOpenClawCatalogSortedById()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Reset(
        [
            new ModelInfo("ollama/seren-qwen:latest", "seren-qwen:latest"),
            new ModelInfo("anthropic/claude-opus", "Claude Opus"),
        ]);

        using var client = _factory.CreateClient();
        var models = await client.GetFromJsonAsync<ModelInfo[]>("/api/models", ct);

        models.ShouldNotBeNull();
        models.Length.ShouldBe(2);
        models[0].Id.ShouldBe("anthropic/claude-opus");
        models[1].Id.ShouldBe("ollama/seren-qwen:latest");
    }

    [Fact]
    public async Task GetAll_CachesResponseWithinShortWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Reset([new ModelInfo("cached/a", null)]);

        using var client = _factory.CreateClient();
        _ = await client.GetFromJsonAsync<ModelInfo[]>("/api/models", ct);

        // Mutate the stub's backing list directly (without Reset, which
        // clears the cache). The second call must serve the cached payload
        // instead of re-querying the stub.
        _factory.MutateOpenClawModels([new ModelInfo("cached/b", null)]);

        var second = await client.GetFromJsonAsync<ModelInfo[]>("/api/models", ct);

        second.ShouldNotBeNull();
        second.Length.ShouldBe(1);
        second[0].Id.ShouldBe("cached/a");
    }

    [Fact]
    public async Task Apply_WritesConfigAndTriggersRefresh()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Reset([new ModelInfo("ollama/seren-qwen:latest", "seren-qwen:latest")]);

        using var client = _factory.CreateClient();

        var res = await client.PostAsJsonAsync(
            "/api/models/apply",
            new { model = "ollama/seren-gemma:latest" },
            ct);

        res.StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);
        var invocations = _factory.GetWriterInvocations();
        invocations.Count.ShouldBe(1);
        invocations[0].ShouldBe("ollama/seren-gemma:latest");
        _factory.GetRefreshCount().ShouldBe(1);
    }

    [Fact]
    public async Task Apply_WithNullModel_ClearsPin()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Reset([new ModelInfo("ollama/seren-qwen:latest", null)]);

        using var client = _factory.CreateClient();

        var res = await client.PostAsJsonAsync(
            "/api/models/apply",
            new { model = (string?)null },
            ct);

        res.StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);
        var invocations = _factory.GetWriterInvocations();
        invocations.Count.ShouldBe(1);
        invocations[0].ShouldBeNull();
    }

    [Fact]
    public async Task Refresh_TriggersUpstreamRescanAndDropsCache()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.Reset([new ModelInfo("before/refresh", null)]);

        using var client = _factory.CreateClient();

        // Prime the cache.
        _ = await client.GetFromJsonAsync<ModelInfo[]>("/api/models", ct);

        // Simulate a model appearing upstream after the cache was built.
        _factory.MutateOpenClawModels([new ModelInfo("after/refresh", null)]);

        var refresh = await client.PostAsync("/api/models/refresh", content: null, ct);
        refresh.StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);
        _factory.GetRefreshCount().ShouldBe(1);

        // Cache cleared → next GET picks up the new list.
        var fresh = await client.GetFromJsonAsync<ModelInfo[]>("/api/models", ct);
        fresh.ShouldNotBeNull();
        fresh.Length.ShouldBe(1);
        fresh[0].Id.ShouldBe("after/refresh");
    }

    public sealed class StubCatalogFactory : SerenTestFactory
    {
        private readonly StubOpenClaw _openClaw = new();
        private readonly StubConfigWriter _writer = new();

        public void Reset(IReadOnlyList<ModelInfo> openClaw)
        {
            _openClaw.Models = openClaw;
            _openClaw.ResetCounters();
            _writer.Reset();

            // Drop the memo cache so each test sees the stub it just set,
            // not a payload cached by a prior test. The endpoint's TTL of
            // 60 s would otherwise leak across the whole class.
            using var scope = Services.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            cache.Remove("models:catalog");
        }

        /// <summary>
        /// Mutates the OpenClaw stub's model list without touching the
        /// endpoint cache. Used by the cache-hit test to prove the second
        /// call serves the cached payload rather than re-querying.
        /// </summary>
        public void MutateOpenClawModels(IReadOnlyList<ModelInfo> models)
        {
            _openClaw.Models = models;
        }

        /// <summary>Number of times the refresh endpoint called the stub.</summary>
        public int GetRefreshCount() => _openClaw.RefreshCount;

        /// <summary>Models passed to the config writer (null = cleared pin).</summary>
        public IReadOnlyList<string?> GetWriterInvocations() => _writer.Invocations;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                ReplaceSingleton(services, typeof(IOpenClawClient), _openClaw);
                ReplaceSingleton(services, typeof(IOpenClawConfigWriter), _writer);
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

        private sealed class StubOpenClaw : IOpenClawClient
        {
            public IReadOnlyList<ModelInfo> Models { get; set; } = Array.Empty<ModelInfo>();
            public int RefreshCount { get; private set; }
            public void ResetCounters() => RefreshCount = 0;
            public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
                => Task.FromResult(Models);
            public Task RefreshCatalogAsync(CancellationToken ct = default)
            {
                RefreshCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class StubConfigWriter : IOpenClawConfigWriter
        {
            private readonly List<string?> _invocations = new();
            public IReadOnlyList<string?> Invocations => _invocations;
            public void Reset() => _invocations.Clear();
            public Task SetDefaultModelAsync(string? model, CancellationToken ct = default)
            {
                _invocations.Add(model);
                return Task.CompletedTask;
            }
        }
    }
}
