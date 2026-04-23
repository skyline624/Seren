using Seren.Contracts.Characters;

namespace Seren.Application.Characters.Personas;

/// <summary>
/// Thrown by the capture pipeline (reader → extractor → handler) when
/// the OpenClaw workspace cannot be turned into a valid Seren
/// <see cref="Seren.Domain.Entities.Character"/>. The endpoint layer
/// maps this to a typed 400 / 404 response body
/// (<see cref="PersonaCaptureErrorResponse"/>) — it is always a
/// user-visible, non-retryable failure, never a 500.
/// </summary>
/// <remarks>
/// Every instance carries one of the <see cref="PersonaCaptureError"/>
/// code constants in <see cref="Code"/>. Kept in parallel to
/// <c>CharacterImportException</c> — same shape, different taxonomy —
/// so capture and import surface uniformly to the UI.
/// </remarks>
public sealed class PersonaCaptureException : Exception
{
    /// <summary>Machine-readable code from <see cref="PersonaCaptureError"/>.</summary>
    public string Code { get; }

    /// <summary>Optional free-form context for debugging / UI.</summary>
    public string? Details { get; }

    public PersonaCaptureException(string code, string message, string? details = null, Exception? inner = null)
        : base(message, inner)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(message);
        Code = code;
        Details = details;
    }
}
