using System.Text.Json;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Seren.Application.Abstractions;
using Seren.Application.Characters;
using Seren.Application.Characters.Import;
using Seren.Application.Characters.Personas;
using Seren.Contracts.Characters;
using Seren.Infrastructure.Persistence.Json;

namespace Seren.Server.Api.Endpoints;

/// <summary>
/// REST endpoints for character CRUD, activation, Character Card v3
/// import, and 2D avatar streaming.
/// </summary>
public static partial class CharacterEndpoints
{
    /// <summary>Hard cap on uploaded card size (mirrors the parser's bound).</summary>
    private const long ImportMaxBytes = 10L * 1024 * 1024;

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

        // The 10 MB cap is enforced inside the handler (Kestrel's default
        // 30 MB request-body limit is a broader guardrail; anything
        // between 10 MB and 30 MB hits the handler and gets rejected
        // there with a typed 413).
        group.MapPost("/import", ImportAsync)
            .WithName("ImportCharacterCard")
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data");

        group.MapGet("/{id:guid}/avatar", GetAvatarAsync).WithName("GetCharacterAvatar");

        // Persona-capture + download (Chantier 7). Capture is the
        // inverse of the writer: read OpenClaw's current IDENTITY.md +
        // SOUL.md and persist them as a new Character. Download
        // serialises an existing Character to JSON for backup/transfer.
        group.MapPost("/capture", CaptureAsync).WithName("CapturePersona");
        group.MapGet("/{id:guid}/download", DownloadAsync).WithName("DownloadCharacter");

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
            body.AvatarModelPath,
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
            body.AvatarModelPath,
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

    /// <summary>
    /// <c>POST /api/characters/import</c> — multipart upload of a
    /// Character Card v3 (.png / .apng / .json). Parses, persists, and
    /// returns the new character. Parser-level failures surface as
    /// typed <see cref="CharacterImportErrorResponse"/> with a 400
    /// status; oversized uploads are 413.
    /// </summary>
    private static async Task<IResult> ImportAsync(
        HttpRequest request,
        IMediator mediator,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new CharacterImportErrorResponse
            {
                Code = CharacterImportError.InvalidCard,
                Message = "Request body must be multipart/form-data with a 'file' part.",
            });
        }

        var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
        var file = form.Files["file"] ?? (form.Files.Count > 0 ? form.Files[0] : null);
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new CharacterImportErrorResponse
            {
                Code = CharacterImportError.InvalidCard,
                Message = "Missing 'file' part in the multipart upload.",
            });
        }

        if (file.Length > ImportMaxBytes)
        {
            return Results.Json(
                new CharacterImportErrorResponse
                {
                    Code = CharacterImportError.CardTooLarge,
                    Message = $"Card exceeds the {ImportMaxBytes / (1024 * 1024)} MB cap.",
                    Details = $"uploaded = {file.Length} bytes",
                },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var activateOnImport = ParseBoolean(form["activateOnImport"].ToString());

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var ms = new MemoryStream(capacity: (int)file.Length);
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            bytes = ms.ToArray();
        }

        try
        {
            var result = await mediator
                .Send(new ImportCharacterCardCommand(bytes, file.FileName, activateOnImport), ct)
                .ConfigureAwait(false);

            return Results.Created(
                $"/api/characters/{result.Character.Id}",
                new ImportedCharacterResponse(result.Character, result.Warnings));
        }
        catch (CharacterImportException ex)
        {
            return Results.BadRequest(new CharacterImportErrorResponse
            {
                Code = ex.Code,
                Message = ex.Message,
                Details = ex.Details,
            });
        }
    }

    /// <summary>
    /// <c>GET /api/characters/{id}/avatar</c> — stream the 2D avatar
    /// PNG for <paramref name="id"/> if the character was imported from
    /// a PNG-backed Character Card. Otherwise returns 404.
    /// </summary>
    private static async Task<IResult> GetAvatarAsync(
        Guid id,
        ICharacterAvatarStore avatarStore,
        CancellationToken ct)
    {
        var stream = await avatarStore.OpenReadAsync(id, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return Results.NotFound();
        }

        // Cache on the URL path — it's stable for a given character id
        // and the record is effectively immutable in v1 (delete + re-import
        // to change the avatar).
        return Results.Stream(
            stream,
            contentType: "image/png",
            enableRangeProcessing: false);
    }

    private static bool ParseBoolean(string? value)
        => !string.IsNullOrEmpty(value)
           && (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>POST /api/characters/capture</c> — read OpenClaw's current
    /// workspace persona (IDENTITY.md + SOUL.md) and persist it as a
    /// new Seren <c>Character</c>. Empty / un-configured workspace →
    /// 404 typed; unparseable markdown → 400 typed. Never 500.
    /// </summary>
    private static async Task<IResult> CaptureAsync(
        HttpRequest request,
        IMediator mediator,
        CancellationToken ct)
    {
        var activate = ParseBoolean(request.Query["activate"].ToString());

        try
        {
            var result = await mediator
                .Send(new CapturePersonaCommand(activate), ct)
                .ConfigureAwait(false);

            return Results.Created(
                $"/api/characters/{result.Character.Id}",
                new CapturedPersonaResponse(result.Character));
        }
        catch (PersonaCaptureException ex) when (ex.Code == PersonaCaptureError.WorkspaceEmpty
                                              || ex.Code == PersonaCaptureError.NoWorkspaceConfigured)
        {
            return Results.Json(
                new PersonaCaptureErrorResponse
                {
                    Code = ex.Code,
                    Message = ex.Message,
                    Details = ex.Details,
                },
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (PersonaCaptureException ex)
        {
            return Results.BadRequest(new PersonaCaptureErrorResponse
            {
                Code = ex.Code,
                Message = ex.Message,
                Details = ex.Details,
            });
        }
    }

    /// <summary>
    /// <c>GET /api/characters/{id}/download</c> — serialise the
    /// Character to JSON using the same AOT-friendly source-gen
    /// context as the repository, with a <c>Content-Disposition</c>
    /// header so the browser surfaces a download prompt.
    /// </summary>
    private static async Task<IResult> DownloadAsync(
        Guid id,
        IMediator mediator,
        CancellationToken ct)
    {
        var characters = await mediator.Send(new GetAllCharactersQuery(), ct).ConfigureAwait(false);
        var character = characters.FirstOrDefault(c => c.Id == id);
        if (character is null)
        {
            return Results.NotFound();
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(character, CharacterJsonContext.Default.Character);
        var fileName = BuildDownloadFilename(character.Name);
        return Results.File(bytes, contentType: "application/json", fileDownloadName: fileName);
    }

    /// <summary>
    /// Slugify the character name for the download filename. ASCII
    /// only + lowercase + hyphen-collapsed — avoids RFC 6266 encoding
    /// surprises on the <c>Content-Disposition</c> header.
    /// </summary>
    internal static string BuildDownloadFilename(string characterName)
    {
        var lowered = (characterName ?? string.Empty).ToLowerInvariant();
        var slug = FilenameSlugRegex().Replace(lowered, "-").Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            slug = "character";
        }
        return $"{slug}.character.json";
    }

    [GeneratedRegex(@"[^a-z0-9-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FilenameSlugRegex();
}

/// <summary>DTO for character creation requests.</summary>
public sealed record CreateCharacterRequest(
    string Name,
    string SystemPrompt,
    string? AvatarModelPath,
    string? Voice,
    string? AgentId);

/// <summary>DTO for character update requests.</summary>
public sealed record UpdateCharacterRequest(
    string Name,
    string SystemPrompt,
    string? AvatarModelPath,
    string? Voice,
    string? AgentId);

/// <summary>
/// Success payload of <c>POST /api/characters/import</c>. Bundles the
/// persisted character with non-fatal parser warnings so the UI can
/// render both the success toast and any advisory notices in one step.
/// </summary>
public sealed record ImportedCharacterResponse(
    Seren.Domain.Entities.Character Character,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Success payload of <c>POST /api/characters/capture</c>. Carries
/// the persisted character so the UI can display the new card
/// immediately — no follow-up <c>GET /api/characters</c> required.
/// </summary>
public sealed record CapturedPersonaResponse(
    Seren.Domain.Entities.Character Character);
