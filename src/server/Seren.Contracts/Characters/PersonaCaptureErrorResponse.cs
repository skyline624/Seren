using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Characters;

/// <summary>
/// Body returned by <c>POST /api/characters/capture</c> on any
/// non-success status code. The UI maps <see cref="Code"/> → i18n key
/// (<c>characters.capture.errors.&lt;code&gt;</c>) to render a
/// user-facing message in the active locale.
/// </summary>
/// <remarks>
/// Shape mirrors <see cref="CharacterImportErrorResponse"/> so the UI
/// can share a single error-toast component across both flows.
/// </remarks>
[ExportTsClass]
public sealed record PersonaCaptureErrorResponse
{
    /// <summary>One of the constants in <see cref="PersonaCaptureError"/>.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable server-side message (English). The UI
    /// normally ignores this in favour of the i18n key derived from
    /// <see cref="Code"/>, but keeps it for logs.</summary>
    public required string Message { get; init; }

    /// <summary>Optional extra context for debugging; never carries
    /// user data or sensitive payload content.</summary>
    public string? Details { get; init; }
}
