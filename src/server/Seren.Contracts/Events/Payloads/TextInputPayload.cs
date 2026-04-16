using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>input:text</c> event sent by a client to submit
/// a text message for processing by the active character.
/// </summary>
[ExportTsClass]
public sealed record TextInputPayload
{
    /// <summary>User's text message.</summary>
    public required string Text { get; init; }

    /// <summary>Optional session identifier for conversation continuity.</summary>
    public Guid? SessionId { get; init; }
}
