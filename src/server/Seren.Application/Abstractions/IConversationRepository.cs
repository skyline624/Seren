using Seren.Domain.Entities;

namespace Seren.Application.Abstractions;

/// <summary>
/// Repository for conversation messages enabling multi-turn context.
/// </summary>
public interface IConversationRepository
{
    /// <summary>
    /// Persists a new conversation message.
    /// </summary>
    Task AddAsync(ConversationMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the most recent messages for a session, ordered chronologically.
    /// </summary>
    /// <param name="sessionId">The session to query.</param>
    /// <param name="limit">Maximum number of messages to return.</param>
    Task<IReadOnlyList<ConversationMessage>> GetBySessionAsync(
        Guid sessionId,
        int limit,
        CancellationToken cancellationToken);
}
