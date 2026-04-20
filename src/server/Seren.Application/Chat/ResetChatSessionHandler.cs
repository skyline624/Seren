using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.OpenClaw.Handlers;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.Chat;

/// <summary>
/// Handles <see cref="ResetChatSessionCommand"/> by rotating Seren's
/// shared session key (so the next chat to OpenClaw lands in a brand-new
/// session with empty LLM context) and broadcasting an
/// <c>output:chat:cleared</c> event so every connected UI flushes its
/// local message list in lockstep. Long-term memory plugins and device
/// pairing are unaffected because they're not bound to the session id.
/// </summary>
/// <remarks>
/// Rotation is preferred over the upstream <c>sessions.reset</c> RPC,
/// which requires <c>operator.admin</c> — a privilege the bootstrap
/// device pairing does not grant. Rotation gives the same user-visible
/// outcome (LLM context cleared, history wiped from the active key) and
/// works with the standard <c>operator.write</c> scope.
/// </remarks>
public sealed class ResetChatSessionHandler : ICommandHandler<ResetChatSessionCommand>
{
    private readonly IChatSessionKeyProvider _sessionKeyProvider;
    private readonly ISerenHub _hub;
    private readonly ILogger<ResetChatSessionHandler> _logger;

    public ResetChatSessionHandler(
        IChatSessionKeyProvider sessionKeyProvider,
        ISerenHub hub,
        ILogger<ResetChatSessionHandler> logger)
    {
        _sessionKeyProvider = sessionKeyProvider;
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(ResetChatSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var newKey = await _sessionKeyProvider.RotateAsync(cancellationToken).ConfigureAwait(false);

        var payload = new ChatClearedPayload
        {
            At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        await _hub.BroadcastAsync(
            OpenClawRelayEnvelope.Create(EventTypes.OutputChatCleared, payload),
            excluding: null,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Chat session reset: rotated to {SessionKey} and broadcast to all connected peers",
            newKey);

        return Unit.Value;
    }
}
