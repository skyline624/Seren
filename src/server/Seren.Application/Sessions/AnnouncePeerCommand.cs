using Mediator;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Sessions;

/// <summary>
/// Command raised when a peer sends a <c>module:announce</c> envelope.
/// </summary>
/// <param name="PeerId">Id of the peer that announced.</param>
/// <param name="Payload">The parsed announce payload.</param>
/// <param name="ParentEventId">Id of the incoming <c>module:announce</c> event for causal tracing.</param>
public sealed record AnnouncePeerCommand(
    PeerId PeerId,
    AnnouncePayload Payload,
    string ParentEventId) : IRequest<AnnouncedPayload>;
