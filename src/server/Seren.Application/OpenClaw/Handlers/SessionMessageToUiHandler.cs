using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.OpenClaw.Notifications;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.OpenClaw.Handlers;

/// <summary>
/// Relays <see cref="SessionMessageReceivedNotification"/> to connected UI
/// peers as an <c>output:session:message</c> broadcast. Lets the avatar
/// react to messages arriving from OpenClaw channel integrations (Discord,
/// Slack, Telegram, …) or from other operator clients.
/// </summary>
public sealed class SessionMessageToUiHandler : INotificationHandler<SessionMessageReceivedNotification>
{
    private readonly ISerenHub _hub;
    private readonly ILogger<SessionMessageToUiHandler> _logger;

    public SessionMessageToUiHandler(ISerenHub hub, ILogger<SessionMessageToUiHandler> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask Handle(
        SessionMessageReceivedNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var payload = new SessionMessagePayload
        {
            SessionKey = notification.SessionKey,
            Role = notification.Role,
            Content = notification.Content,
            Timestamp = notification.Timestamp,
            Author = notification.Author,
            Channel = notification.Channel,
            Seq = notification.Seq,
        };

        _logger.LogDebug(
            "Relaying session.message for session {SessionKey} (role={Role}, channel={Channel}) to UI peers.",
            notification.SessionKey, notification.Role, notification.Channel ?? "<internal>");

        await _hub.BroadcastAsync(
            OpenClawRelayEnvelope.Create(EventTypes.OutputSessionMessage, payload),
            excluding: null,
            cancellationToken).ConfigureAwait(false);
    }
}
