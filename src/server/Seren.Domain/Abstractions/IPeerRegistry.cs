using Seren.Domain.Entities;
using Seren.Domain.ValueObjects;

namespace Seren.Domain.Abstractions;

/// <summary>
/// Write-side contract of the peer registry. Extends
/// <see cref="IReadOnlyPeerRegistry"/> with mutation operations.
/// </summary>
public interface IPeerRegistry : IReadOnlyPeerRegistry
{
    /// <summary>
    /// Inserts a brand-new <see cref="Peer"/>.
    /// </summary>
    /// <returns><c>true</c> if the peer was added, <c>false</c> if an entry with the same id already existed.</returns>
    bool Add(Peer peer);

    /// <summary>
    /// Removes a peer (typically on connection close).
    /// </summary>
    bool Remove(PeerId id);

    /// <summary>
    /// Replaces the snapshot of an existing peer atomically (copy-on-write).
    /// </summary>
    /// <returns><c>true</c> if the peer was updated, <c>false</c> if no peer with that id exists.</returns>
    bool Update(Peer updated);
}
