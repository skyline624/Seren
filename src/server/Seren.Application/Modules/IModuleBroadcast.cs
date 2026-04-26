using Mediator;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Modules;

/// <summary>
/// Marker for module-emitted notifications that should be forwarded to all
/// connected peers as a WebSocket envelope. Implementations are typically
/// records that carry the event-type discriminator + the payload to wrap.
/// </summary>
/// <remarks>
/// <para>
/// The host registers a single generic
/// <c>ModuleBroadcastHandler&lt;T&gt;</c> (Mediator <see cref="INotificationHandler{TNotification}"/>)
/// that translates every <see cref="IModuleBroadcast"/> implementation into
/// a <c>WebSocketEnvelope</c> and forwards it through <c>ISerenHub</c>.
/// One handler, N modules — DRY by design.
/// </para>
/// <para>
/// Modules that need a more nuanced delivery (per-session, per-role, etc.)
/// can publish a regular Mediator <see cref="INotification"/> instead and
/// register their own handler — the broadcast contract stays narrow on
/// purpose.
/// </para>
/// </remarks>
public interface IModuleBroadcast : INotification
{
    /// <summary>
    /// WebSocket event type, in <c>domain:subdomain[:verb]</c> form
    /// (e.g. <c>"weather:updated"</c>). Routed to UI peers as the envelope
    /// <c>type</c>.
    /// </summary>
    string EventType { get; }

    /// <summary>
    /// Payload object to serialise into the envelope <c>data</c>. The host
    /// uses <c>JsonSerializer.SerializeToElement</c> so payloads must be
    /// JSON-serialisable; module authors should use plain records to keep
    /// the wire shape stable.
    /// </summary>
    object Payload { get; }

    /// <summary>
    /// Optional peer to exclude from the broadcast (e.g. echo-suppressing
    /// the originator on a chat reply). <see langword="null"/> broadcasts
    /// to every authenticated peer.
    /// </summary>
    PeerId? ExcludingPeer { get; }
}
