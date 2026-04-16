using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Seren.Application.Abstractions;

namespace Seren.Server.Api.Endpoints;

/// <summary>
/// REST endpoint to list available OpenClaw models/agents.
/// </summary>
public static class ModelEndpoints
{
    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/models").WithTags("models");

        group.MapGet("/", GetAllAsync).WithName("GetAllModels");

        return routes;
    }

    private static async Task<IResult> GetAllAsync(IOpenClawClient openClaw, CancellationToken ct)
    {
        var models = await openClaw.GetModelsAsync(ct).ConfigureAwait(false);
        return Results.Ok(models);
    }
}
