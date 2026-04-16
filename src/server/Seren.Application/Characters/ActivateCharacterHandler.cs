using Mediator;
using Seren.Application.Abstractions;

namespace Seren.Application.Characters;

/// <summary>
/// Handles <see cref="ActivateCharacterCommand"/> by setting the target
/// character as active and deactivating all others via the repository.
/// </summary>
public sealed class ActivateCharacterHandler : ICommandHandler<ActivateCharacterCommand>
{
    private readonly ICharacterRepository _repository;

    public ActivateCharacterHandler(ICharacterRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<Unit> Handle(ActivateCharacterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var character = await _repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Character '{command.Id}' not found.");

        await _repository.SetActiveAsync(character.Id, cancellationToken);

        return Unit.Value;
    }
}
