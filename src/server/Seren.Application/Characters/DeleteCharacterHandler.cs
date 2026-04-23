using Mediator;
using Seren.Application.Abstractions;

namespace Seren.Application.Characters;

/// <summary>
/// Handles <see cref="DeleteCharacterCommand"/> by removing the character
/// from the repository and cleaning up any 2D avatar left over from a
/// previous Character Card import. Throws if the character does not
/// exist; the avatar cleanup is idempotent and never surfaces an error
/// to the caller (already-absent file is a legitimate state, not a bug).
/// </summary>
public sealed class DeleteCharacterHandler : ICommandHandler<DeleteCharacterCommand>
{
    private readonly ICharacterRepository _repository;
    private readonly ICharacterAvatarStore _avatarStore;

    public DeleteCharacterHandler(
        ICharacterRepository repository,
        ICharacterAvatarStore avatarStore)
    {
        _repository = repository;
        _avatarStore = avatarStore;
    }

    public async ValueTask<Unit> Handle(DeleteCharacterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var deleted = await _repository.DeleteAsync(command.Id, cancellationToken);
        if (!deleted)
        {
            throw new InvalidOperationException($"Character '{command.Id}' not found.");
        }

        // Fire-and-forget cleanup — the store's DeleteAsync is idempotent
        // and swallows IO failures internally, so this call cannot mask
        // the successful repository delete.
        await _avatarStore.DeleteAsync(command.Id, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
