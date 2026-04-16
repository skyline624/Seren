using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:chat:chunk</c> event broadcast by the hub
/// during a streaming chat completion.
/// </summary>
[ExportTsClass]
public sealed record ChatChunkPayload
{
    /// <summary>Partial text content of the streamed chunk (markers already removed).</summary>
    public required string Content { get; init; }

    /// <summary>Optional character identifier that produced the response.</summary>
    public string? CharacterId { get; init; }
}
