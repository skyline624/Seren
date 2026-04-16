using Seren.Domain.ValueObjects;

namespace Seren.Domain.Entities;

/// <summary>
/// A peer represents a single WebSocket connection to the Seren hub
/// (UI client, plugin, external module).
/// </summary>
/// <remarks>
/// <see cref="Peer"/> is an immutable snapshot. Mutations go through
/// <c>IPeerRegistry</c> which returns a new instance.
/// </remarks>
public sealed record Peer(
    PeerId Id,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastHeartbeatAt,
    bool IsAuthenticated,
    ModuleIdentity? Identity,
    int MissedHeartbeats)
{
    /// <summary>
    /// Creates a brand-new peer that has just connected and has not yet authenticated
    /// nor announced its identity.
    /// </summary>
    public static Peer CreateNew(PeerId id, DateTimeOffset now, bool authRequired) => new(
        Id: id,
        ConnectedAt: now,
        LastHeartbeatAt: now,
        IsAuthenticated: !authRequired,
        Identity: null,
        MissedHeartbeats: 0);

    /// <summary>
    /// Returns a new snapshot with <see cref="IsAuthenticated"/> set to <c>true</c>.
    /// </summary>
    public Peer Authenticate() => this with { IsAuthenticated = true };

    /// <summary>
    /// Returns a new snapshot attaching the module identity after a <c>module:announce</c> event.
    /// </summary>
    public Peer Announce(ModuleIdentity identity) => this with { Identity = identity };

    /// <summary>
    /// Returns a new snapshot with an updated heartbeat timestamp and a cleared miss counter.
    /// </summary>
    public Peer Beat(DateTimeOffset now) => this with { LastHeartbeatAt = now, MissedHeartbeats = 0 };
}
