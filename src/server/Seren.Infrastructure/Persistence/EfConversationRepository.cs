using Microsoft.EntityFrameworkCore;
using Seren.Application.Abstractions;
using Seren.Domain.Entities;

namespace Seren.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IConversationRepository"/>
/// backed by <see cref="SerenDbContext"/>.
/// </summary>
public sealed class EfConversationRepository : IConversationRepository
{
    private readonly SerenDbContext _db;

    public EfConversationRepository(SerenDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ConversationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        _db.ConversationMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetBySessionAsync(
        Guid sessionId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await _db.ConversationMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .OrderBy(m => m.CreatedAt) // chronological for LLM context
            .ToListAsync(cancellationToken);
    }
}
