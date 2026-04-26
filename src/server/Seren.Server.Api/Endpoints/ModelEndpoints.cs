using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;

namespace Seren.Server.Api.Endpoints;

/// <summary>
/// REST endpoints around the LLM model catalog. <c>GET /api/models</c>
/// proxies OpenClaw's <c>models.list</c> RPC (with a 60 s memo cache) and
/// <c>POST /api/models/refresh</c> forces OpenClaw to rescan its provider
/// catalogs via the <c>gateway</c> admin tool — useful after
/// <c>ollama pull</c> when the new tag wouldn't otherwise appear until
/// the next process restart.
/// </summary>
public static class ModelEndpoints
{
    internal const string CacheKey = "models:catalog";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/models").WithTags("models");

        group.MapGet("/", GetAllAsync).WithName("GetAllModels");
        group.MapPost("/refresh", RefreshAsync).WithName("RefreshModels");
        group.MapPost("/apply", ApplyAsync).WithName("ApplyModel");

        return routes;
    }

    private static async Task<IResult> GetAllAsync(
        IOpenClawClient openClaw,
        IMemoryCache cache,
        CancellationToken ct)
    {
        if (cache.TryGetValue<IReadOnlyList<ModelInfo>>(CacheKey, out var cached) && cached is not null)
        {
            return Results.Ok(cached);
        }

        var catalog = await openClaw.GetModelsAsync(ct).ConfigureAwait(false);

        // Alphabetical order makes the UI model dropdown deterministic
        // across reloads (same provider grouping, same model order).
        var ordered = catalog
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToArray();

        // Only memoise non-empty results: right after a refresh (or during
        // gateway startup) the RPC may briefly return an empty list while
        // OpenClaw is still rebuilding its catalog. Caching [] for 60 s
        // would wedge the UI on an empty dropdown until the TTL expires.
        if (ordered.Length > 0)
        {
            cache.Set(CacheKey, (IReadOnlyList<ModelInfo>)ordered, CacheTtl);
        }
        return Results.Ok(ordered);
    }

    private static async Task<IResult> RefreshAsync(
        IOpenClawClient openClaw,
        IMemoryCache cache,
        ILogger<RefreshEndpointMarker> logger,
        CancellationToken ct)
    {
        try
        {
            await openClaw.RefreshCatalogAsync(ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Model catalog refresh rejected by OpenClaw.");
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Model catalog refresh failed",
                detail: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not reach OpenClaw to refresh the model catalog.");
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "OpenClaw unreachable",
                detail: ex.Message);
        }

        // Drop the 60 s memoised list so the next GET picks up the fresh
        // catalog as soon as the gateway finishes its post-restart handshake.
        cache.Remove(CacheKey);

        // 202 Accepted: the gateway schedules the SIGUSR1 self-restart with
        // a 2 s delay; callers should wait ~5 s before re-requesting the list.
        return Results.Accepted();
    }

    private static async Task<IResult> ApplyAsync(
        ApplyModelRequest request,
        IOpenClawClient openClaw,
        IMemoryCache cache,
        ILogger<ApplyEndpointMarker> logger,
        CancellationToken ct)
    {
        // The gateway tool's `config.patch` action atomically merges the
        // patch, validates against the schema, and decides hot-reload vs
        // SIGUSR1 self-restart on its own — so a single call replaces the
        // earlier write-file + RefreshCatalog two-step.
        try
        {
            await openClaw.SetDefaultModelAsync(request.Model, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Model apply rejected by OpenClaw gateway.");
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Model apply rejected by OpenClaw",
                detail: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not reach OpenClaw to apply the model.");
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "OpenClaw unreachable",
                detail: ex.Message);
        }

        cache.Remove(CacheKey);
        return Results.Accepted();
    }

    /// <summary>
    /// Body of <c>POST /api/models/apply</c>. A <c>null</c> model clears
    /// the pin and lets the gateway fall back to <c>${OPENCLAW_DEFAULT_MODEL}</c>.
    /// </summary>
    public sealed record ApplyModelRequest(string? Model);

    // Marker types for ILogger<T> category — keep log names explicit
    // without a circular reference to the static endpoint class.
    private sealed class RefreshEndpointMarker;
    private sealed class ApplyEndpointMarker;
}
