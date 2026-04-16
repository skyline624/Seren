using Mediator;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Query to list all characters.
/// </summary>
public sealed record GetAllCharactersQuery : IQuery<IReadOnlyList<Character>>;
