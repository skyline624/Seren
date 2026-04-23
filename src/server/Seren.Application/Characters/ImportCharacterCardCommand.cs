using Mediator;
using Seren.Domain.Entities;

namespace Seren.Application.Characters;

/// <summary>
/// Command: import a Character Card v3 (.png / .apng / .json) and turn
/// it into a new <see cref="Character"/> in the local library.
/// </summary>
/// <param name="FileBytes">Raw bytes of the uploaded card. Must be the
/// whole file — the parser dispatches on magic bytes, not extension.</param>
/// <param name="FileName">Original filename from the upload — used
/// strictly for log / error enrichment. Never touches the filesystem.</param>
/// <param name="ActivateOnImport">When true, the newly-imported character
/// becomes the active persona immediately (convenient when the user
/// imports a card they intend to talk to right away).</param>
public sealed record ImportCharacterCardCommand(
    byte[] FileBytes,
    string FileName,
    bool ActivateOnImport = false) : ICommand<ImportedCharacterResult>;

/// <summary>Result of a successful <see cref="ImportCharacterCardCommand"/>.</summary>
/// <param name="Character">The newly-created persisted character.</param>
/// <param name="Warnings">Non-fatal issues surfaced by the parser —
/// e.g. <c>"prompt_truncated"</c>, <c>"lorebook_deferred"</c>. The UI
/// maps each to an i18n key.</param>
public sealed record ImportedCharacterResult(
    Character Character,
    IReadOnlyList<string> Warnings);
