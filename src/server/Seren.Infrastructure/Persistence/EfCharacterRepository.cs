using Microsoft.EntityFrameworkCore;
using Seren.Application.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ICharacterRepository"/>
/// backed by <see cref="SerenDbContext"/>.
/// </summary>
public sealed class EfCharacterRepository : ICharacterRepository
{
    private readonly SerenDbContext _db;

    public EfCharacterRepository(SerenDbContext db)
    {
        _db = db;
    }

    public async Task<Character?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.Characters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Character?> GetActiveAsync(CancellationToken cancellationToken)
    {
        return await _db.Characters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<Character>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _db.Characters
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Character character, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);

        _db.Characters.Add(character);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Character character, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);

        var existing = await _db.Characters.FindAsync([character.Id], cancellationToken)
            ?? throw new InvalidOperationException($"Character '{character.Id}' not found.");

        _db.Entry(existing).CurrentValues.SetValues(character);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await _db.Characters
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return rows > 0;
    }

    public async Task SetActiveAsync(Guid id, CancellationToken cancellationToken)
    {
        // Deactivate all, then activate the target — two queries, atomic via SaveChanges.
        await _db.Characters
            .Where(c => c.IsActive)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.IsActive, false), cancellationToken);

        var rows = await _db.Characters
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.IsActive, true), cancellationToken);

        if (rows == 0)
        {
            throw new InvalidOperationException($"Character '{id}' not found.");
        }
    }
}
