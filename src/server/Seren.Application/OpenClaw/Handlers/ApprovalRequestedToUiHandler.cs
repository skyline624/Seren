using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.OpenClaw.Notifications;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.OpenClaw.Handlers;

/// <summary>
/// Relays <see cref="ApprovalRequestedNotification"/> to connected UI peers
/// as an <c>output:approval:request</c> broadcast. The UI can then prompt
/// the operator to allow or deny the pending action.
/// </summary>
public sealed class ApprovalRequestedToUiHandler : INotificationHandler<ApprovalRequestedNotification>
{
    private readonly ISerenHub _hub;
    private readonly ILogger<ApprovalRequestedToUiHandler> _logger;

    public ApprovalRequestedToUiHandler(ISerenHub hub, ILogger<ApprovalRequestedToUiHandler> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask Handle(
        ApprovalRequestedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var payload = new ApprovalRequestPayload
        {
            Id = notification.Id,
            Kind = notification.Kind,
            Title = notification.Title,
            Summary = notification.Summary,
            Command = notification.Command,
            CreatedAtMs = notification.CreatedAtMs,
            ExpiresAtMs = notification.ExpiresAtMs,
            SourceChannel = notification.SourceChannel,
        };

        _logger.LogInformation(
            "Relaying approval request {ApprovalId} (kind={Kind}) to UI peers.",
            notification.Id, notification.Kind);

        await _hub.BroadcastAsync(
            OpenClawRelayEnvelope.Create(EventTypes.OutputApprovalRequest, payload),
            excluding: null,
            cancellationToken).ConfigureAwait(false);
    }
}
