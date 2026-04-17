using System.Text.Json;

namespace Seren.Infrastructure.OpenClaw.Gateway;

/// <summary>
/// Gateway-RPC abstraction consumed by the Application layer. Callers send
/// a method name + optional params and await a JSON payload back. The
/// underlying connection is kept alive by a <see cref="BackgroundService"/>
/// implementation; callers don't manage the socket lifecycle.
/// </summary>
public interface IOpenClawGateway
{
    /// <summary>Current connection status.</summary>
    OpenClawGatewayStatus Status { get; }

    /// <summary>
    /// Invoke a gateway RPC method (e.g. <c>chat.history</c>,
    /// <c>sessions.list</c>, <c>channels.status</c>).
    /// </summary>
    /// <param name="method">Gateway method name.</param>
    /// <param name="parameters">Optional method parameters — serialized to JSON with the default options.</param>
    /// <param name="cancellationToken">Cancellation propagated to the pending call.</param>
    /// <param name="timeout">Per-call timeout; defaults to <c>OpenClaw:WebSocket:RpcTimeout</c>.</param>
    /// <returns>The gateway's <c>payload</c> JSON element (may be <see cref="JsonValueKind.Undefined"/> when the gateway returns no body).</returns>
    /// <exception cref="OpenClawGatewayException">The gateway replied with an error frame.</exception>
    /// <exception cref="InvalidOperationException">The gateway is not currently ready.</exception>
    /// <exception cref="OperationCanceledException">Either the caller or the timeout cancelled the pending call.</exception>
    Task<JsonElement> CallAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null);
}

/// <summary>
/// Connection lifecycle states of <see cref="IOpenClawGateway"/>.
/// </summary>
public enum OpenClawGatewayStatus
{
    /// <summary>Host hasn't started, or the loop has just exited.</summary>
    Disconnected = 0,

    /// <summary>TCP/WS handshake in progress (HTTP upgrade).</summary>
    Connecting = 1,

    /// <summary>Socket open, waiting for <c>hello-ok</c> response to our <c>connect</c> request.</summary>
    Handshaking = 2,

    /// <summary>Handshake complete — RPC calls are accepted and server events are flowing.</summary>
    Ready = 3,

    /// <summary>Previous session dropped; backoff timer is counting down to the next attempt.</summary>
    Reconnecting = 4,
}
