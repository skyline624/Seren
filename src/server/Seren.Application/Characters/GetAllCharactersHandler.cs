using Mediator;
using Seren.Application.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Handles <see cref="GetAllCharactersQuery"/> by returning
/// every character stored in the repository.
/// </summary>
public sealed class GetAllCharactersHandler : IQueryHandler<GetAllCharactersQuery, IReadOnlyList<Character>>
{
    private readonly ICharacterRepository _repository;

    public GetAllCharactersHandler(ICharacterRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<IReadOnlyList<Character>> Handle(GetAllCharactersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _repository.GetAllAsync(cancellationToken);
    }
}
