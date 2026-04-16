using System.Net.WebSockets;
using Seren.Domain.ValueObjects;

namespace Seren.Infrastructure.Realtime;

/// <summary>
/// Thread-safe in-memory mapping from <see cref="PeerId"/> to the live
/// <see cref="WebSocket"/> that serves that peer.
/// </summary>
public sealed class WebSocketConnectionRegistry : IWebSocketConnectionRegistry
{
    private readonly ConcurrentDictionary<PeerId, WebSocket> _sockets = new();

    public bool TryRegister(PeerId peerId, WebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        return _sockets.TryAdd(peerId, socket);
    }

    public bool TryUnregister(PeerId peerId) => _sockets.TryRemove(peerId, out _);

    public bool TryGet(PeerId peerId, out WebSocket? socket)
    {
        if (_sockets.TryGetValue(peerId, out var found))
        {
            socket = found;
            return true;
        }

        socket = null;
        return false;
    }

    public IReadOnlyList<(PeerId Id, WebSocket Socket)> Snapshot() =>
        _sockets.Select(kvp => (kvp.Key, kvp.Value)).ToArray();
}
