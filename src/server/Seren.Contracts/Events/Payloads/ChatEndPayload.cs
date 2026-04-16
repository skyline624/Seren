using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>output:chat:end</c> event broadcast by the hub
/// when a streaming chat completion has finished.
/// </summary>
[ExportTsClass]
public sealed record ChatEndPayload
{
    /// <summary>Optional character identifier that produced the response.</summary>
    public string? CharacterId { get; init; }
}
