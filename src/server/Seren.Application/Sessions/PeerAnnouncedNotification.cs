using Mediator;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Sessions;

/// <summary>
/// Fired by <see cref="AnnouncePeerHandler"/> right after a peer's identity
/// is registered in the peer registry. Lets cross-cutting concerns (chat
/// history hydration, presence broadcast, …) react to a freshly-known
/// client without polluting the announce request/response flow.
/// </summary>
/// <param name="PeerId">Identifier of the connection that just announced.</param>
/// <param name="ModuleId">Self-declared module instance identifier.</param>
/// <param name="PluginId">Optional self-declared plugin identifier.</param>
public sealed record PeerAnnouncedNotification(
    PeerId PeerId,
    string ModuleId,
    string? PluginId) : INotification;
