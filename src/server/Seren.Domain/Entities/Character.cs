namespace Seren.Domain.Entities;

/// <summary>
/// A character represents an AI persona orchestrated by the Seren hub,
/// rendered as a Live2D avatar with a configured voice and personality.
/// </summary>
/// <remarks>
/// <see cref="Character"/> is an immutable record. Mutations produce new
/// instances via <c>with</c> expressions and are persisted through
/// <c>ICharacterRepository</c>.
/// <para/>
/// The primary constructor carries the "core" persona fields present since
/// day 1. Optional metadata harvested from imported Character Card v3
/// files — greeting, description, 2D avatar, tags, opaque import blob —
/// live as separate <c>init</c>-only properties so existing callers and
/// the JSON store keep working unchanged: old records deserialise with
/// the new properties at their defaults, new records round-trip with
/// every field preserved.
/// </remarks>
public sealed record Character(
    Guid Id,
    string Name,
    string SystemPrompt,
    string? AvatarModelPath,
    string? Voice,
    string? AgentId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// First-message greeting from a Character Card v3 import (<c>first_mes</c>
    /// in the spec). UI can render it as the opening bubble on a freshly
    /// activated character. Null on hand-authored characters created via
    /// the settings form.
    /// </summary>
    public string? Greeting { get; init; }

    /// <summary>
    /// Free-form description harvested from a Character Card v3 import.
    /// Not part of the LLM prompt (the parser already folds
    /// <c>description + personality + scenario</c> into <see cref="SystemPrompt"/>
    /// when no explicit <c>system_prompt</c> is present); kept here for
    /// display purposes and for a future round-trip export.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Path (relative to the avatar store root) of the 2D avatar PNG
    /// extracted from an imported card. Served via
    /// <c>GET /api/characters/{id}/avatar</c>. Distinct from
    /// <see cref="AvatarModelPath"/>: this is a raw 2D portrait image
    /// harvested from a Character Card v3, while
    /// <see cref="AvatarModelPath"/> points at a rigged Live2D model.
    /// </summary>
    public string? AvatarImagePath { get; init; }

    /// <summary>
    /// Tags from an imported Character Card v3 (<c>data.tags</c>). Purely
    /// informational today; future filtering / search will consume them.
    /// Defaults to an empty list so callers never have to null-check.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Opaque JSON blob storing the Character Card v3 fields Seren does
    /// not yet interpret: <c>alternate_greetings</c>,
    /// <c>character_book</c> (keyword-activated lore entries),
    /// <c>mes_example</c>, <c>post_history_instructions</c>,
    /// <c>creator_notes</c>. Persisted verbatim to enable a lossless
    /// round-trip export and to unlock a future "lorebook runtime"
    /// chantier without re-importing the card.
    /// </summary>
    public string? ImportMetadataJson { get; init; }

    /// <summary>
    /// Creates a brand-new character with a fresh <see cref="Id"/>,
    /// <see cref="IsActive"/> set to <c>false</c>, and timestamps
    /// initialised to the current moment.
    /// </summary>
    public static Character Create(string name, string systemPrompt) => new(
        Id: Guid.NewGuid(),
        Name: name,
        SystemPrompt: systemPrompt,
        AvatarModelPath: null,
        Voice: null,
        AgentId: null,
        IsActive: false,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    /// <summary>
    /// Creates a brand-new character from the fields harvested by the
    /// Character Card v3 parser. Keeps the card→domain mapping inside
    /// the domain (SRP) so the import handler only orchestrates.
    /// </summary>
    /// <param name="name">Display name (CCv3 <c>data.name</c>).</param>
    /// <param name="systemPrompt">Composed prompt per CCv3 semantics —
    /// <c>data.system_prompt</c> if present, otherwise
    /// <c>description + personality + scenario</c>, plus any
    /// <c>character_book</c> entries flagged <c>constant: true</c>.</param>
    /// <param name="greeting">Optional first-message (<c>data.first_mes</c>).</param>
    /// <param name="description">Optional raw description for UI display.</param>
    /// <param name="tags">Tags harvested from <c>data.tags</c>. Never null —
    /// pass <see cref="Array.Empty{T}"/> when absent.</param>
    /// <param name="avatarImagePath">Relative path inside the avatar store
    /// once the PNG has been persisted; null for .json-only cards.</param>
    /// <param name="importMetadataJson">Opaque JSON blob preserving the
    /// uninterpreted CCv3 fields.</param>
    public static Character CreateFromCard(
        string name,
        string systemPrompt,
        string? greeting,
        string? description,
        IReadOnlyList<string> tags,
        string? avatarImagePath,
        string? importMetadataJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentNullException.ThrowIfNull(tags);

        var now = DateTimeOffset.UtcNow;
        return new Character(
            Id: Guid.NewGuid(),
            Name: name,
            SystemPrompt: systemPrompt,
            AvatarModelPath: null,
            Voice: null,
            AgentId: null,
            IsActive: false,
            CreatedAt: now,
            UpdatedAt: now)
        {
            Greeting = greeting,
            Description = description,
            AvatarImagePath = avatarImagePath,
            Tags = tags,
            ImportMetadataJson = importMetadataJson,
        };
    }
}
