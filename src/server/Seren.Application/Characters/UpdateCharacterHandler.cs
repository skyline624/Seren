using Mediator;
using Seren.Application.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Handles <see cref="UpdateCharacterCommand"/> by replacing the character's
/// mutable properties while preserving identity and timestamps.
/// </summary>
public sealed class UpdateCharacterHandler : ICommandHandler<UpdateCharacterCommand, Character>
{
    private readonly ICharacterRepository _repository;

    public UpdateCharacterHandler(ICharacterRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<Character> Handle(UpdateCharacterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Character '{command.Id}' not found.");

        var updated = existing with
        {
            Name = command.Name,
            SystemPrompt = command.SystemPrompt,
            AvatarModelPath = command.AvatarModelPath,
            Voice = command.Voice,
            AgentId = command.AgentId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await _repository.UpdateAsync(updated, cancellationToken);

        return updated;
    }
}
