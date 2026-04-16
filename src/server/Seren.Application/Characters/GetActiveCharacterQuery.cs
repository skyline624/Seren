using Mediator;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Query to retrieve the currently active character.
/// Returns <c>null</c> when no character is active.
/// </summary>
public sealed record GetActiveCharacterQuery : IQuery<Character?>;
