using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Seren.Application.Abstractions;

namespace Seren.Server.Api.Endpoints;

/// <summary>
/// REST endpoint that lists every model the Seren stack can route a chat
/// to. Aggregates two sources in parallel:
/// <list type="bullet">
///   <item>OpenClaw's cloud catalog via <see cref="IOpenClawClient"/>.</item>
///   <item>Locally-installed Ollama models via <see cref="IOllamaClient"/>.</item>
/// </list>
/// The merge is memoised for a short window because users typically open
/// the Settings drawer several times in a session and the upstream lists
/// don't change at sub-minute cadence.
/// </summary>
public static class ModelEndpoints
{
    private const string CacheKey = "models:merged";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/models").WithTags("models");

        group.MapGet("/", GetAllAsync).WithName("GetAllModels");

        return routes;
    }

    private static async Task<IResult> GetAllAsync(
        IOpenClawClient openClaw,
        IOllamaClient ollama,
        IMemoryCache cache,
        CancellationToken ct)
    {
        if (cache.TryGetValue<IReadOnlyList<ModelInfo>>(CacheKey, out var cached) && cached is not null)
        {
            return Results.Ok(cached);
        }

        // Run both upstream calls in parallel — total latency is bounded by
        // the slower of the two rather than their sum. Both implementations
        // degrade to an empty list on failure, so `Task.WhenAll` never throws.
        var openClawTask = openClaw.GetModelsAsync(ct);
        var ollamaTask = ollama.GetLocalModelsAsync(ct);
        await Task.WhenAll(openClawTask, ollamaTask).ConfigureAwait(false);

        var merged = openClawTask.Result
            .Concat(ollamaTask.Result)
            // De-dup by id: a model id collision across sources is rare, but
            // if it happens, keeping the first occurrence (OpenClaw-cloud
            // wins) is deterministic.
            .GroupBy(m => m.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            // Alphabetical order makes the UI cascade dropdown deterministic
            // across reloads (same provider grouping, same model order).
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToArray();

        cache.Set(CacheKey, (IReadOnlyList<ModelInfo>)merged, CacheTtl);
        return Results.Ok(merged);
    }
}
