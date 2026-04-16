using Seren.Domain.Abstractions;
using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;

namespace Seren.Infrastructure.Realtime;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IPeerRegistry"/>
/// backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </summary>
/// <remarks>
/// Registered as a singleton; lives for the whole lifetime of the host process.
/// When the hub scales out to multiple instances a distributed registry
/// (Redis, Orleans…) will replace this implementation — the interface does not
/// change.
/// </remarks>
public sealed class InMemoryPeerRegistry : IPeerRegistry
{
    private readonly ConcurrentDictionary<PeerId, Peer> _peers = new();

    public int Count => _peers.Count;

    public bool TryGet(PeerId id, out Peer? peer)
    {
        if (_peers.TryGetValue(id, out var found))
        {
            peer = found;
            return true;
        }

        peer = null;
        return false;
    }

    public IReadOnlyCollection<Peer> Snapshot() => _peers.Values.ToArray();

    public bool Add(Peer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return _peers.TryAdd(peer.Id, peer);
    }

    public bool Remove(PeerId id) => _peers.TryRemove(id, out _);

    public bool Update(Peer updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        while (_peers.TryGetValue(updated.Id, out var current))
        {
            if (_peers.TryUpdate(updated.Id, updated, current))
            {
                return true;
            }
        }

        return false;
    }
}
