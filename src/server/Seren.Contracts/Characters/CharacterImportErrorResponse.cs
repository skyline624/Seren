using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Characters;

/// <summary>
/// Body returned by <c>POST /api/characters/import</c> on any non-success
/// status code. The UI maps <see cref="Code"/> → i18n key to render a
/// user-facing message that matches the active locale.
/// </summary>
[ExportTsClass]
public sealed record CharacterImportErrorResponse
{
    /// <summary>One of the constants in <see cref="CharacterImportError"/>.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable English fallback message. Not shown to the
    /// end user when the UI knows the <see cref="Code"/> — the UI prefers
    /// the localised string. Used in log output and as a last-resort
    /// display when the client is out of date.</summary>
    public required string Message { get; init; }

    /// <summary>Optional free-form context (which field was empty, which
    /// spec string was seen, …). Safe to expose — no user data.</summary>
    public string? Details { get; init; }
}
