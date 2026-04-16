using Mediator;
using Seren.Application.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Handles <see cref="GetActiveCharacterQuery"/> by returning the
/// currently active character or <c>null</c> if none is active.
/// </summary>
public sealed class GetActiveCharacterHandler : IQueryHandler<GetActiveCharacterQuery, Character?>
{
    private readonly ICharacterRepository _repository;

    public GetActiveCharacterHandler(ICharacterRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<Character?> Handle(GetActiveCharacterQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _repository.GetActiveAsync(cancellationToken);
    }
}
