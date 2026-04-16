using TypeGen.Core.TypeAnnotations;

namespace Seren.Contracts.Events.Payloads;

/// <summary>
/// Payload of a <c>module:authenticate</c> event sent by a client
/// to authenticate its WebSocket session with a JWT token.
/// </summary>
[ExportTsClass]
public sealed record AuthenticatePayload
{
    /// <summary>JWT bearer token.</summary>
    public required string Token { get; init; }
}

/// <summary>
/// Payload of a <c>module:authenticated</c> event sent by the server
/// to confirm successful authentication.
/// </summary>
[ExportTsClass]
public sealed record AuthenticatedPayload
{
    /// <summary>The authenticated peer's identifier.</summary>
    public required string PeerId { get; init; }

    /// <summary>The role assigned to the peer (e.g. "user", "admin").</summary>
    public string? Role { get; init; }
}
