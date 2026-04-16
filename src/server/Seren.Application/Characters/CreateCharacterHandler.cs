using Mediator;
using Seren.Application.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Handles <see cref="CreateCharacterCommand"/> by instantiating a new
/// <see cref="Character"/> via its factory method, applying optional
/// properties, and persisting it through <see cref="ICharacterRepository"/>.
/// </summary>
public sealed class CreateCharacterHandler : ICommandHandler<CreateCharacterCommand, Character>
{
    private readonly ICharacterRepository _repository;

    public CreateCharacterHandler(ICharacterRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<Character> Handle(CreateCharacterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var character = Character.Create(command.Name, command.SystemPrompt);

        character = character with
        {
            VrmAssetPath = command.VrmAssetPath,
            Voice = command.Voice,
            AgentId = command.AgentId,
        };

        await _repository.AddAsync(character, cancellationToken);

        return character;
    }
}
