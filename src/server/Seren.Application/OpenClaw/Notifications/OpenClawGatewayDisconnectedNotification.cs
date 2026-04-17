using Mediator;

namespace Seren.Application.OpenClaw.Notifications;

/// <summary>
/// Published when the outbound WebSocket to OpenClaw has dropped — whether
/// because the gateway sent a close frame, the handshake timed out, or the
/// tick watchdog fired. The client will automatically reconnect; this
/// notification is informational so downstream handlers can surface status
/// to the UI or flush in-flight state.
/// </summary>
/// <param name="Reason">Human-readable reason why the connection ended.</param>
/// <param name="WasHandshakeComplete">True if the gateway had sent a successful <c>hello-ok</c> before the drop.</param>
public sealed record OpenClawGatewayDisconnectedNotification(
    string Reason,
    bool WasHandshakeComplete) : INotification;
