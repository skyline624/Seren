using Mediator;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Chat;

/// <summary>
/// Loads the current persisted chat transcript for the main session and
/// pushes it to a single peer via <c>output:chat:history:item</c> events,
/// terminated by <c>output:chat:history:end</c>. Used both for the initial
/// hydration when a peer announces and for scroll-back pagination.
/// </summary>
/// <param name="TargetPeer">Peer that receives the items (unicast).</param>
/// <param name="Before">
/// When set, only messages whose <c>messageId</c> is older than this value
/// are sent. <c>null</c> for the initial hydration request.
/// </param>
/// <param name="Limit">Maximum number of messages to send.</param>
public sealed record LoadChatHistoryCommand(
    PeerId TargetPeer,
    string? Before,
    int Limit) : ICommand;
