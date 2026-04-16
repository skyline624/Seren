using Seren.Domain.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Application.Abstractions;

/// <summary>
/// Extended character repository contract that adds read/write operations
/// on top of the Domain's read-only <see cref="Domain.Abstractions.ICharacterRepository"/>.
/// Implemented by the infrastructure layer (DIP).
/// </summary>
public interface ICharacterRepository : Domain.Abstractions.ICharacterRepository
{
    /// <summary>
    /// Retrieves a character by its unique identifier.
    /// </summary>
    Task<Character?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all characters.
    /// </summary>
    Task<IReadOnlyList<Character>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists a new character.
    /// </summary>
    Task AddAsync(Character character, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing character.
    /// </summary>
    Task UpdateAsync(Character character, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the character with the given <paramref name="id"/> as active
    /// and deactivates all others.
    /// </summary>
    Task SetActiveAsync(Guid id, CancellationToken cancellationToken);
}
