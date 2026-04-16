using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of a <c>transport:connection:heartbeat</c> event.
/// </summary>
[ExportTsClass]
public sealed record HeartbeatPayload
{
    /// <summary>Either <c>"ping"</c> (client → hub) or <c>"pong"</c> (hub → client).</summary>
    public required string Kind { get; init; }

    /// <summary>Timestamp (Unix ms) at which the frame was emitted.</summary>
    public long At { get; init; }
}
