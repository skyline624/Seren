using System.Net.WebSockets;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Minimal abstraction over <see cref="ClientWebSocket"/> used by the
/// gateway handshake + RPC + event loop. Exists so the handshake,
/// tick monitor and RPC layers can be tested against an in-memory
/// queue-backed double without spinning up a real WebSocket server.
/// </summary>
internal interface IGatewaySocket : IAsyncDisposable
{
    /// <summary>Current socket state, mirrored from the underlying transport.</summary>
    WebSocketState State { get; }

    /// <summary>Send a UTF-8 text frame.</summary>
    Task SendTextAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    /// <summary>
    /// Receive the next frame. Returns a close descriptor when the peer
    /// initiates a close; the caller is responsible for answering.
    /// </summary>
    Task<GatewayReceiveResult> ReceiveTextAsync(CancellationToken cancellationToken);

    /// <summary>Initiate a close handshake.</summary>
    Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of <see cref="IGatewaySocket.ReceiveTextAsync"/> — either a text
/// payload or a close indication. Binary frames never appear because the
/// gateway protocol is JSON-over-text only.
/// </summary>
internal readonly record struct GatewayReceiveResult(
    string? Text,
    bool IsClose,
    WebSocketCloseStatus? CloseStatus,
    string? CloseStatusDescription);
