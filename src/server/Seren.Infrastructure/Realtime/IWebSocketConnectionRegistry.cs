using System.Net.WebSockets;
using Seren.Domain.ValueObjects;

namespace Seren.Infrastructure.Realtime;

/// <summary>
/// Infrastructure-level contract mapping a <see cref="PeerId"/> to the
/// underlying <see cref="WebSocket"/>. Kept out of the <c>Seren.Application</c>
/// layer — application code depends on <see cref="Application.Abstractions.ISerenHub"/>
/// instead, which is the only abstraction the business layer should see.
/// </summary>
public interface IWebSocketConnectionRegistry
{
    /// <summary>Registers a socket for a peer. Returns <c>false</c> if a socket is already registered.</summary>
    bool TryRegister(PeerId peerId, WebSocket socket);

    /// <summary>Removes a registered socket.</summary>
    bool TryUnregister(PeerId peerId);

    /// <summary>Looks up a registered socket.</summary>
    bool TryGet(PeerId peerId, out WebSocket? socket);

    /// <summary>Enumerates a snapshot of registered sockets.</summary>
    IReadOnlyList<(PeerId Id, WebSocket Socket)> Snapshot();
}
