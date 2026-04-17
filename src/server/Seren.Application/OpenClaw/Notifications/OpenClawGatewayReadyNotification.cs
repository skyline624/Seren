using Mediator;

namespace Seren.Application.OpenClaw.Notifications;

/// <summary>
/// Published when Seren's outbound WebSocket to OpenClaw has completed the
/// handshake and the gateway's <c>hello-ok</c> response has been accepted.
/// Downstream handlers may subscribe to trigger post-handshake work
/// (history hydration, feature capability checks, etc.).
/// </summary>
/// <param name="ProtocolVersion">Negotiated gateway protocol version.</param>
/// <param name="ServerVersion">Version string reported by the gateway.</param>
/// <param name="ConnectionId">Gateway-assigned connection identifier.</param>
/// <param name="TickIntervalMs">Server tick interval — clients idle longer than twice this value are presumed stale.</param>
/// <param name="Methods">RPC methods advertised by the gateway.</param>
/// <param name="Events">Event names advertised by the gateway.</param>
public sealed record OpenClawGatewayReadyNotification(
    int ProtocolVersion,
    string ServerVersion,
    string ConnectionId,
    int TickIntervalMs,
    IReadOnlyList<string> Methods,
    IReadOnlyList<string> Events) : INotification;
