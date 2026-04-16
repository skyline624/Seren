using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;

namespace Seren.Domain.Abstractions;

/// <summary>
/// Read-side contract of the peer registry (ISP — segregated from write operations
/// so that consumers which only observe the registry can be injected without
/// accidentally mutating it).
/// </summary>
public interface IReadOnlyPeerRegistry
{
    /// <summary>
    /// Number of currently connected peers.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Attempts to retrieve a peer by id.
    /// </summary>
    /// <returns><c>true</c> if the peer exists, <c>false</c> otherwise.</returns>
    bool TryGet(PeerId id, out Peer? peer);

    /// <summary>
    /// Enumerates a snapshot of all currently connected peers.
    /// The returned sequence is safe to iterate while the registry mutates.
    /// </summary>
    IReadOnlyCollection<Peer> Snapshot();
}
