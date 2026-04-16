using Seren.Domain.Abstractions;
using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Tests.Sessions;

/// <summary>
/// Deterministic <see cref="IPeerRegistry"/> test double. Prefer this over NSubstitute
/// when the interface uses <c>out</c> parameters — mocking them is awkward and fragile.
/// </summary>
internal sealed class FakePeerRegistry : IPeerRegistry
{
    private readonly Dictionary<PeerId, Peer> _peers = [];

    public int Count => _peers.Count;

    public int UpdateCalls { get; private set; }

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
        if (_peers.ContainsKey(peer.Id))
        {
            return false;
        }

        _peers[peer.Id] = peer;
        return true;
    }

    public bool Remove(PeerId id) => _peers.Remove(id);

    public bool Update(Peer updated)
    {
        UpdateCalls++;
        if (!_peers.ContainsKey(updated.Id))
        {
            return false;
        }

        _peers[updated.Id] = updated;
        return true;
    }
}
