using Seren.Contracts.Characters;

namespace Seren.Application.Characters.Import;

/// <summary>
/// Thrown by the Character Card parser and related import code when the
/// input cannot be turned into a valid <see cref="Seren.Domain.Entities.Character"/>.
/// The endpoint layer maps this to a 400 / 413 response with a typed
/// <see cref="CharacterImportErrorResponse"/> body — it is always a
/// user-visible, non-retryable failure, never an internal server error.
/// </summary>
/// <remarks>
/// Every instance carries one of the <see cref="CharacterImportError"/>
/// code constants in <see cref="Code"/>. Optional <see cref="Details"/>
/// is surfaced verbatim in the response body but never carries user data
/// or sensitive payload content.
/// </remarks>
public sealed class CharacterImportException : Exception
{
    /// <summary>Machine-readable error code from <see cref="CharacterImportError"/>.</summary>
    public string Code { get; }

    /// <summary>Optional free-form context for debugging / UI.</summary>
    public string? Details { get; }

    public CharacterImportException(string code, string message, string? details = null, Exception? inner = null)
        : base(message, inner)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(message);
        Code = code;
        Details = details;
    }
}
