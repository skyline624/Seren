namespace Seren.Domain.Entities;

/// <summary>
/// A single message in a conversation session, stored for multi-turn context.
/// </summary>
public sealed record ConversationMessage(
    Guid Id,
    Guid SessionId,
    string Role,
    string Content,
    Guid? CharacterId,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Creates a new conversation message with a fresh identifier.
    /// </summary>
    public static ConversationMessage Create(
        Guid sessionId,
        string role,
        string content,
        Guid? characterId = null) => new(
        Id: Guid.NewGuid(),
        SessionId: sessionId,
        Role: role,
        Content: content,
        CharacterId: characterId,
        CreatedAt: DateTimeOffset.UtcNow);
}
