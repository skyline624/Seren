using Seren.Application.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Infrastructure.OpenClaw;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ICharacterRepository"/>
/// backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </summary>
/// <remarks>
/// For Phase 2 only — will be replaced by EF Core persistence when multi-user
/// storage requirements appear.
/// </remarks>
public sealed class InMemoryCharacterRepository : ICharacterRepository
{
    private readonly ConcurrentDictionary<Guid, Character> _characters = new();

    public Task<Character?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _characters.TryGetValue(id, out var character);
        return Task.FromResult(character);
    }

    public Task<Character?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var active = _characters.Values.FirstOrDefault(c => c.IsActive);
        return Task.FromResult(active);
    }

    public Task<IReadOnlyList<Character>> GetAllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Character>>(_characters.Values.ToArray());
    }

    public Task AddAsync(Character character, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);
        _characters.TryAdd(character.Id, character);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Character character, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);
        _characters.AddOrUpdate(character.Id, character, (_, _) => character);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var removed = _characters.TryRemove(id, out _);
        return Task.FromResult(removed);
    }

    public Task SetActiveAsync(Guid id, CancellationToken cancellationToken)
    {
        // Deactivate all characters, then activate the requested one.
        foreach (var kvp in _characters)
        {
            if (kvp.Key == id)
            {
                _characters.TryUpdate(kvp.Key, kvp.Value with { IsActive = true }, kvp.Value);
            }
            else if (kvp.Value.IsActive)
            {
                _characters.TryUpdate(kvp.Key, kvp.Value with { IsActive = false }, kvp.Value);
            }
        }

        return Task.CompletedTask;
    }
}
