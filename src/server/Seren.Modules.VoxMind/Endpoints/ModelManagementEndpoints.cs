using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Seren.Modules.VoxMind.Models;

namespace Seren.Modules.VoxMind.Endpoints;

/// <summary>
/// Minimal-API surface for the VoxMind model manager: list, download,
/// delete, poll-progress. Lives outside the WebSocket because filesystem
/// operations (especially deletion) need an unambiguous request/response
/// shape and a stable URL the UI can address with <c>fetch</c>.
/// </summary>
public static class ModelManagementEndpoints
{
    public static IEndpointRouteBuilder MapVoxMindModelEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/voxmind/models").WithTags("voxmind");

        group.MapGet("/", ListAsync).WithName("ListVoxMindModels");
        group.MapPost("/{id}/download", StartDownloadAsync).WithName("StartVoxMindModelDownload");
        group.MapGet("/{id}/download/status", GetDownloadStatusAsync).WithName("GetVoxMindModelDownloadStatus");
        group.MapDelete("/{id}", DeleteAsync).WithName("DeleteVoxMindModel");

        return routes;
    }

    private static IResult ListAsync(
        IModelStorage storage,
        IModelDownloadService downloads)
    {
        var entries = new List<ModelEntryDto>(ModelCatalog.All.Count);
        for (var i = 0; i < ModelCatalog.All.Count; i++)
        {
            var v = ModelCatalog.All[i];
            entries.Add(BuildEntry(v, storage, downloads));
        }

        return Results.Ok(entries);
    }

    private static IResult StartDownloadAsync(
        string id,
        IModelDownloadService downloads)
    {
        var variant = ModelCatalog.Find(id);
        if (variant is null)
        {
            return Results.NotFound(new { error = $"Unknown model id '{id}'." });
        }

        if (variant.IsSystemManaged)
        {
            return Results.Conflict(new { error = $"Model '{id}' is system-managed and cannot be downloaded from the UI." });
        }

        var state = downloads.Start(variant);
        return Results.Accepted($"/api/voxmind/models/{id}/download/status", new
        {
            id,
            status = state.Status.ToString().ToLowerInvariant(),
        });
    }

    private static IResult GetDownloadStatusAsync(
        string id,
        IModelDownloadService downloads)
    {
        var variant = ModelCatalog.Find(id);
        if (variant is null)
        {
            return Results.NotFound(new { error = $"Unknown model id '{id}'." });
        }

        var state = downloads.Snapshot(variant);
        return Results.Ok(new ModelDownloadStateDto(
            state.Status.ToString().ToLowerInvariant(),
            state.BytesDone,
            state.BytesTotal,
            state.Error));
    }

    private static async Task<IResult> DeleteAsync(
        string id,
        IModelStorage storage,
        IModelDownloadService downloads,
        CancellationToken ct)
    {
        var variant = ModelCatalog.Find(id);
        if (variant is null)
        {
            return Results.NotFound(new { error = $"Unknown model id '{id}'." });
        }

        if (variant.IsSystemManaged)
        {
            return Results.Conflict(new { error = $"Model '{id}' is system-managed and cannot be deleted from the UI." });
        }

        // Server-side guard mirroring the UI: never let the user wipe
        // the last available engine.
        var others = 0;
        for (var i = 0; i < ModelCatalog.All.Count; i++)
        {
            var other = ModelCatalog.All[i];
            if (string.Equals(other.Id, id, StringComparison.Ordinal))
            {
                continue;
            }

            if (storage.IsDownloaded(other))
            {
                others++;
            }
        }

        if (others == 0)
        {
            return Results.Conflict(new
            {
                error = "Refusing to delete the last available STT model.",
            });
        }

        try
        {
            await storage.DeleteAsync(variant, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Failed to delete model bundle");
        }

        downloads.Forget(id);
        return Results.NoContent();
    }

    private static ModelEntryDto BuildEntry(
        ModelVariant v, IModelStorage storage, IModelDownloadService downloads)
    {
        var downloaded = storage.IsDownloaded(v);
        var state = downloads.Snapshot(v);
        ModelDownloadStateDto? dto = state.Status == ModelDownloadStatus.Idle
            ? null
            : new ModelDownloadStateDto(
                state.Status.ToString().ToLowerInvariant(),
                state.BytesDone,
                state.BytesTotal,
                state.Error);

        return new ModelEntryDto(
            v.Id,
            v.EngineFamily,
            v.DisplayKey,
            v.ApproxSizeMb,
            downloaded,
            v.IsSystemManaged,
            dto);
    }
}

/// <summary>
/// Wire-format snapshot of one model variant — mirrors the catalog plus
/// runtime presence + active download state. The UI consumes this from
/// <c>GET /api/voxmind/models</c>.
/// </summary>
public sealed record ModelEntryDto(
    string Id,
    string EngineFamily,
    string DisplayKey,
    int ApproxSizeMb,
    bool IsDownloaded,
    bool IsSystemManaged,
    ModelDownloadStateDto? Download);

/// <summary>Wire-format download state. Status is lower-cased for JS-friendliness.</summary>
public sealed record ModelDownloadStateDto(
    string Status,
    long BytesDone,
    long BytesTotal,
    string? Error);
