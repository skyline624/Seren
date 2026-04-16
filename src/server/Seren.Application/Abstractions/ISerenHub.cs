using Seren.Contracts.Events;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Abstractions;

/// <summary>
/// Application-layer contract for sending messages to connected peers.
/// Implemented by the infrastructure (<c>SerenWebSocketHub</c>).
/// </summary>
/// <remarks>
/// Mediator handlers should depend on this abstraction (DIP) instead of
/// referencing <c>System.Net.WebSockets</c> directly.
/// </remarks>
public interface ISerenHub
{
    /// <summary>
    /// Sends an envelope to a single peer.
    /// </summary>
    /// <returns><c>true</c> if the peer was connected and the message was queued; <c>false</c> otherwise.</returns>
    Task<bool> SendAsync(PeerId peerId, WebSocketEnvelope envelope, CancellationToken cancellationToken);

    /// <summary>
    /// Broadcasts an envelope to every connected and authenticated peer except the given one.
    /// </summary>
    /// <returns>The number of peers that received the envelope.</returns>
    Task<int> BroadcastAsync(WebSocketEnvelope envelope, PeerId? excluding, CancellationToken cancellationToken);
}
