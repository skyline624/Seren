namespace Seren.Application.Characters.Import;

/// <summary>
/// Normalised in-memory view of a Character Card v2/v3 payload, as
/// emitted by <see cref="ICharacterCardParser"/>. Transport-agnostic:
/// whether the source was a .json file or a PNG/APNG with embedded
/// tEXt chunks, the parser yields the same shape.
/// </summary>
/// <param name="SpecVersion">Verbatim value of the source <c>spec</c>
/// field — <c>"chara_card_v3"</c> or <c>"chara_card_v2"</c>.</param>
/// <param name="Name">Display name (CCv3 <c>data.name</c>, non-empty).</param>
/// <param name="SystemPrompt">Fully composed prompt — never empty,
/// never null. The parser applies the CCv3 composition rules
/// (explicit <c>system_prompt</c> → fallback concat of
/// <c>description / personality / scenario</c>) and appends any
/// <c>character_book.entries</c> flagged <c>constant: true</c>, then
/// substitutes <c>{{char}}</c> → <paramref name="Name"/>,
/// <c>{{user}}</c> → literal <c>"user"</c>. Truncated to 4000
/// characters to satisfy the existing <c>SystemPrompt</c> validator —
/// a truncation warning is added to <see cref="Warnings"/> when this
/// happens.</param>
/// <param name="Greeting">First-message greeting (<c>data.first_mes</c>)
/// after macro substitution. Null when the card has none.</param>
/// <param name="Description">Raw <c>data.description</c> kept for UI
/// display alongside the system prompt.</param>
/// <param name="Tags">Tags harvested from <c>data.tags</c>. Never null —
/// empty list when the card has none.</param>
/// <param name="Creator">Optional <c>data.creator</c>.</param>
/// <param name="CharacterVersion">Optional <c>data.character_version</c>.</param>
/// <param name="AvatarPng">Raw PNG bytes when the source was a PNG/APNG
/// card; null for .json-only imports.</param>
/// <param name="ImportMetadataJson">Opaque JSON blob preserving the
/// CCv3 fields the current version of Seren does not interpret
/// (<c>alternate_greetings</c>, <c>character_book</c>, <c>mes_example</c>,
/// <c>post_history_instructions</c>, <c>creator_notes</c>). Never null —
/// empty JSON object <c>"{}"</c> when nothing to record, so downstream
/// code can always parse it.</param>
/// <param name="Warnings">Non-fatal issues surfaced to the UI as
/// informational toasts (e.g. <c>"prompt truncated to 4000 chars"</c>,
/// <c>"lorebook entries stored but not yet used"</c>).</param>
public sealed record CharacterCardData(
    string SpecVersion,
    string Name,
    string SystemPrompt,
    string? Greeting,
    string? Description,
    IReadOnlyList<string> Tags,
    string? Creator,
    string? CharacterVersion,
    byte[]? AvatarPng,
    string ImportMetadataJson,
    IReadOnlyList<string> Warnings);
