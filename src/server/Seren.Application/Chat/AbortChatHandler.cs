using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;

namespace Seren.Application.Chat;

/// <summary>
/// Handles <see cref="AbortChatCommand"/> by forwarding the abort request
/// to OpenClaw via <see cref="IOpenClawChat.AbortAsync"/>. The streaming
/// handler's <c>finally</c> block already emits <c>OutputChatEnd</c> when
/// the run terminates, so this handler intentionally does not broadcast
/// anything — keeping the user-Stop path indistinguishable from a clean
/// end of stream from the UI's point of view.
/// </summary>
public sealed class AbortChatHandler : ICommandHandler<AbortChatCommand>
{
    private readonly IOpenClawChat _openClawChat;
    private readonly IChatSessionKeyProvider _sessionKeyProvider;
    private readonly IChatRunRegistry _runRegistry;
    private readonly ILogger<AbortChatHandler> _logger;

    public AbortChatHandler(
        IOpenClawChat openClawChat,
        IChatSessionKeyProvider sessionKeyProvider,
        IChatRunRegistry runRegistry,
        ILogger<AbortChatHandler> logger)
    {
        _openClawChat = openClawChat;
        _sessionKeyProvider = sessionKeyProvider;
        _runRegistry = runRegistry;
        _logger = logger;
    }

    public async ValueTask<Unit> Handle(AbortChatCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionKey = _sessionKeyProvider.MainSessionKey;
        var runId = request.RunId ?? _runRegistry.GetActiveRun(sessionKey);

        if (string.IsNullOrEmpty(runId))
        {
            // No active run: most likely the stream finished between the
            // user clicking Stop and the frame arriving here. No-op.
            _logger.LogDebug(
                "AbortChatCommand for session {SessionKey} ignored — no active run.",
                sessionKey);
            return Unit.Value;
        }

        _logger.LogInformation(
            "Aborting chat run {RunId} on session {SessionKey} (user-initiated)",
            runId, sessionKey);

        await _openClawChat.AbortAsync(sessionKey, runId, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
