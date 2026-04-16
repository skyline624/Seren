namespace Seren.Domain.Entities;

/// <summary>
/// A character represents an AI persona orchestrated by the Seren hub,
/// rendered as a VRM/Live2D avatar with a configured voice and personality.
/// </summary>
/// <remarks>
/// <see cref="Character"/> is an immutable record. Mutations produce new
/// instances via <c>with</c> expressions and are persisted through
/// <c>ICharacterRepository</c>.
/// </remarks>
public sealed record Character(
    Guid Id,
    string Name,
    string SystemPrompt,
    string? VrmAssetPath,
    string? Voice,
    string? AgentId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Creates a brand-new character with a fresh <see cref="Id"/>,
    /// <see cref="IsActive"/> set to <c>false</c>, and timestamps
    /// initialised to the current moment.
    /// </summary>
    public static Character Create(string name, string systemPrompt) => new(
        Id: Guid.NewGuid(),
        Name: name,
        SystemPrompt: systemPrompt,
        VrmAssetPath: null,
        Voice: null,
        AgentId: null,
        IsActive: false,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);
}
