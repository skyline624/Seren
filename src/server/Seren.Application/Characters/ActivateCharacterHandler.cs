using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;

namespace Seren.Application.Characters;

/// <summary>
/// Handles <see cref="ActivateCharacterCommand"/> by setting the target
/// character as active, deactivating all others, and refreshing the
/// OpenClaw workspace persona files (<c>IDENTITY.md</c> / <c>SOUL.md</c>)
/// so the next <c>chat.send</c> picks up the new persona.
/// </summary>
/// <remarks>
/// The persona write is <b>best-effort</b>: a failure to write the
/// workspace (missing mount, permission error, disk full) logs a
/// warning but does not fail the activation — the UI still sees the
/// character flip active, and the next chat keeps running with whatever
/// persona is already on disk.
/// </remarks>
public sealed class ActivateCharacterHandler : ICommandHandler<ActivateCharacterCommand>
{
    private readonly ICharacterRepository _repository;
    private readonly IPersonaWorkspaceWriter _personaWriter;
    private readonly ILogger<ActivateCharacterHandler> _logger;

    public ActivateCharacterHandler(
        ICharacterRepository repository,
        IPersonaWorkspaceWriter personaWriter,
        ILogger<ActivateCharacterHandler> logger)
    {
        _repository = repository;
        _personaWriter = personaWriter;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(ActivateCharacterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var character = await _repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Character '{command.Id}' not found.");

        await _repository.SetActiveAsync(character.Id, cancellationToken);

        try
        {
            await _personaWriter.WritePersonaAsync(character, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Best-effort: activation must not fail on persona-write errors.
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Persona workspace refresh failed for character {Character} ({Id}) — activation succeeded, next chat keeps the previous persona.",
                character.Name, character.Id);
        }
#pragma warning restore CA1031

        return Unit.Value;
    }
}
