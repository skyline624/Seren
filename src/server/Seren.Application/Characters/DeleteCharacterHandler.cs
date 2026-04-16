using Mediator;
using Seren.Application.Abstractions;

namespace Seren.Application.Characters;

/// <summary>
/// Handles <see cref="DeleteCharacterCommand"/> by removing the character
/// from the repository. Throws if the character does not exist.
/// </summary>
public sealed class DeleteCharacterHandler : ICommandHandler<DeleteCharacterCommand>
{
    private readonly ICharacterRepository _repository;

    public DeleteCharacterHandler(ICharacterRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<Unit> Handle(DeleteCharacterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var deleted = await _repository.DeleteAsync(command.Id, cancellationToken);
        if (!deleted)
        {
            throw new InvalidOperationException($"Character '{command.Id}' not found.");
        }

        return Unit.Value;
    }
}
