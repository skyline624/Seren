using Seren.Contracts.Events;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Modules;

/// <summary>
/// Opt-in module capability: claims a WebSocket envelope-type prefix and
/// processes every inbound frame that matches it. The host's session
/// processor iterates the registered handlers in priority order and
/// dispatches the first one whose <see cref="TypePrefix"/> matches.
/// </summary>
/// <remarks>
/// <para>
/// Authentication is enforced by the host BEFORE dispatch — handlers can
/// assume the peer is authenticated (when auth is required) and skip
/// re-validating the gate.
/// </para>
/// <para>
/// Handlers should treat exceptions as recoverable: the host wraps each
/// dispatch in a safety net that translates failures into <c>error</c>
/// frames sent back to the peer; only the receive loop's cancellation is
/// considered terminal.
/// </para>
/// </remarks>
public interface IInboundEnvelopeHandler
{
    /// <summary>
    /// Envelope type prefix this handler claims (e.g. <c>"weather:"</c>).
    /// The first handler whose prefix matches the incoming envelope's
    /// <see cref="WebSocketEnvelope.Type"/> wins; full equality matches
    /// trivially when the prefix equals the type.
    /// </summary>
    string TypePrefix { get; }

    /// <summary>
    /// Whether the handler may run before the peer is authenticated. Only
    /// transport heartbeats and the authenticate handshake itself need
    /// this — every other handler defaults to <see langword="false"/> so
    /// the host's auth gate stays effective.
    /// </summary>
    bool AllowUnauthenticated => false;

    /// <summary>
    /// Whether the handler should be detached onto the thread pool so the
    /// receive loop stays responsive during long-running operations
    /// (LLM streaming, audio uploads). Sync handlers (auth, heartbeat,
    /// announce) keep ordering guarantees and return <see langword="false"/>.
    /// </summary>
    bool DetachFromReceiveLoop => false;

    /// <summary>
    /// Processes a single inbound envelope from the given peer.
    /// </summary>
    Task HandleAsync(PeerId peerId, WebSocketEnvelope envelope, CancellationToken cancellationToken);
}
