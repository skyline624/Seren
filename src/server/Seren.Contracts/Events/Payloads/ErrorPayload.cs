using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of an <c>error</c> event sent by the hub to a client.
/// </summary>
[ExportTsClass]
public sealed record ErrorPayload
{
    /// <summary>Human-readable error message.</summary>
    public required string Message { get; init; }

    /// <summary>Optional machine-readable error code.</summary>
    public string? Code { get; init; }
}
