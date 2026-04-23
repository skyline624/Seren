using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.Characters.Personas;
using Seren.Contracts.Characters;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Orchestrates <see cref="CapturePersonaCommand"/> — pure composition
/// over abstractions (DIP) : reader, extractor, repository, metrics.
/// No I/O, no markdown parsing, no system-time access happens here.
/// </summary>
/// <remarks>
/// Flow :
/// <list type="number">
/// <item><description>
///   <see cref="IPersonaWorkspaceReader.ReadCurrentPersonaAsync"/> →
///   translate the tri-state outcome into either a
///   <see cref="PersonaCaptureException"/> or a loaded snapshot.
/// </description></item>
/// <item><description>
///   <see cref="PersonaTemplateExtractor.Extract"/> → pure markdown
///   → primitives mapping ; throws on invalid scaffolding.
/// </description></item>
/// <item><description>
///   <see cref="Character.CreateFromCard"/> keeps the
///   primitives→domain mapping inside the domain (SRP).
/// </description></item>
/// <item><description>
///   Persist via <see cref="ICharacterRepository.AddAsync"/>; flip
///   active if asked.
/// </description></item>
/// <item><description>Record <c>outcome="ok"</c> on success or the
///   exception's <c>Code</c> on failure before rethrowing.</description></item>
/// </list>
/// </remarks>
public sealed class CapturePersonaHandler
    : ICommandHandler<CapturePersonaCommand, CapturedPersonaResult>
{
    private readonly IPersonaWorkspaceReader _reader;
    private readonly ICharacterRepository _repository;
    private readonly IPersonaCaptureMetrics _metrics;
    private readonly ILogger<CapturePersonaHandler> _logger;

    public CapturePersonaHandler(
        IPersonaWorkspaceReader reader,
        ICharacterRepository repository,
        IPersonaCaptureMetrics metrics,
        ILogger<CapturePersonaHandler> logger)
    {
        _reader = reader;
        _repository = repository;
        _metrics = metrics;
        _logger = logger;
    }

    public async ValueTask<CapturedPersonaResult> Handle(CapturePersonaCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var read = await _reader.ReadCurrentPersonaAsync(cancellationToken).ConfigureAwait(false);

            var snapshot = read.Outcome switch
            {
                PersonaReadOutcome.Loaded => read.Snapshot!,
                PersonaReadOutcome.Empty => throw new PersonaCaptureException(
                    PersonaCaptureError.WorkspaceEmpty,
                    "No IDENTITY.md or SOUL.md in the configured workspace."),
                PersonaReadOutcome.NotConfigured => throw new PersonaCaptureException(
                    PersonaCaptureError.NoWorkspaceConfigured,
                    "No OpenClaw:WorkspacePath is configured on this server."),
                _ => throw new PersonaCaptureException(
                    PersonaCaptureError.InvalidPersona,
                    $"Unexpected workspace read outcome: {read.Outcome}."),
            };

            var extracted = PersonaTemplateExtractor.Extract(snapshot);

            var character = Character.CreateFromCard(
                name: extracted.Name,
                systemPrompt: extracted.SystemPrompt,
                greeting: extracted.Greeting,
                description: extracted.Description,
                tags: extracted.Tags,
                avatarImagePath: null,
                importMetadataJson: SerializeCaptureMetadata());

            await _repository.AddAsync(character, cancellationToken).ConfigureAwait(false);

            if (command.ActivateOnCapture)
            {
                await _repository.SetActiveAsync(character.Id, cancellationToken).ConfigureAwait(false);
            }

            _metrics.RecordCapture(outcome: "ok");
            _logger.LogInformation(
                "Captured persona {Character} (id={Id}, activate={Activate})",
                character.Name, character.Id, command.ActivateOnCapture);

            return new CapturedPersonaResult(character);
        }
        catch (PersonaCaptureException ex)
        {
            _metrics.RecordCapture(outcome: ex.Code);
            _logger.LogWarning(
                "Persona capture rejected: {Code} — {Message} (details={Details})",
                ex.Code, ex.Message, ex.Details);
            throw;
        }
    }

    // Opaque JSON tag distinguishing captures from CCv3 imports, so a
    // future UI can show the provenance of each Character card. Stable
    // shape: { source, capturedAt } — no secrets, no workspace path.
    private static string SerializeCaptureMetadata()
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "captured",
            ["capturedAt"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
        return JsonSerializer.Serialize(payload, CaptureMetadataJsonContext.Default.DictionaryStringString);
    }
}

/// <summary>AOT-friendly serializer for the tiny capture metadata tag.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CaptureMetadataJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
