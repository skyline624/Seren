using Seren.Domain.Entities;

namespace Seren.Domain.Abstractions;

/// <summary>
/// Repository contract for <see cref="Character"/> entities.
/// </summary>
public interface ICharacterRepository
{
    /// <summary>
    /// Returns the currently active character, or <c>null</c> if none is configured.
    /// </summary>
    Task<Character?> GetActiveAsync(CancellationToken cancellationToken);
}
