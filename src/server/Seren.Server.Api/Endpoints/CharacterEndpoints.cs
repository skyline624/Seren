using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Seren.Application.Characters;

namespace Seren.Server.Api.Endpoints;

/// <summary>
/// REST endpoints for character CRUD and activation.
/// </summary>
public static class CharacterEndpoints
{
    public static IEndpointRouteBuilder MapCharacterEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/characters").WithTags("characters");

        group.MapGet("/", GetAllAsync).WithName("GetAllCharacters");
        group.MapGet("/active", GetActiveAsync).WithName("GetActiveCharacter");
        group.MapPost("/", CreateAsync).WithName("CreateCharacter");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateCharacter");
        group.MapPost("/{id:guid}/activate", ActivateAsync).WithName("ActivateCharacter");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteCharacter");

        return routes;
    }

    private static async Task<IResult> GetAllAsync(IMediator mediator, CancellationToken ct)
    {
        var characters = await mediator.Send(new GetAllCharactersQuery(), ct).ConfigureAwait(false);
        return Results.Ok(characters);
    }

    private static async Task<IResult> GetActiveAsync(IMediator mediator, CancellationToken ct)
    {
        var character = await mediator.Send(new GetActiveCharacterQuery(), ct).ConfigureAwait(false);
        return character is not null ? Results.Ok(character) : Results.NoContent();
    }

    private static async Task<IResult> CreateAsync(CreateCharacterRequest body, IMediator mediator, CancellationToken ct)
    {
        var command = new CreateCharacterCommand(
            body.Name,
            body.SystemPrompt,
            body.VrmAssetPath,
            body.Voice,
            body.AgentId);

        var character = await mediator.Send(command, ct).ConfigureAwait(false);
        return Results.Created($"/api/characters/{character.Id}", character);
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateCharacterRequest body, IMediator mediator, CancellationToken ct)
    {
        var command = new UpdateCharacterCommand(
            id,
            body.Name,
            body.SystemPrompt,
            body.VrmAssetPath,
            body.Voice,
            body.AgentId);

        var character = await mediator.Send(command, ct).ConfigureAwait(false);
        return Results.Ok(character);
    }

    private static async Task<IResult> ActivateAsync(Guid id, IMediator mediator, CancellationToken ct)
    {
        await mediator.Send(new ActivateCharacterCommand(id), ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, IMediator mediator, CancellationToken ct)
    {
        await mediator.Send(new DeleteCharacterCommand(id), ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}

/// <summary>DTO for character creation requests.</summary>
public sealed record CreateCharacterRequest(
    string Name,
    string SystemPrompt,
    string? VrmAssetPath,
    string? Voice,
    string? AgentId);

/// <summary>DTO for character update requests.</summary>
public sealed record UpdateCharacterRequest(
    string Name,
    string SystemPrompt,
    string? VrmAssetPath,
    string? Voice,
    string? AgentId);
