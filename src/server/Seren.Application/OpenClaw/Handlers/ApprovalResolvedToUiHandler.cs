using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.OpenClaw.Notifications;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.OpenClaw.Handlers;

/// <summary>
/// Relays <see cref="ApprovalResolvedNotification"/> to connected UI peers
/// as an <c>output:approval:resolved</c> broadcast so the UI can dismiss
/// any pending prompt and reflect the decision.
/// </summary>
public sealed class ApprovalResolvedToUiHandler : INotificationHandler<ApprovalResolvedNotification>
{
    private readonly ISerenHub _hub;
    private readonly ILogger<ApprovalResolvedToUiHandler> _logger;

    public ApprovalResolvedToUiHandler(ISerenHub hub, ILogger<ApprovalResolvedToUiHandler> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask Handle(
        ApprovalResolvedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var payload = new ApprovalResolvedPayload
        {
            Id = notification.Id,
            Kind = notification.Kind,
            Decision = notification.Decision,
            ResolvedBy = notification.ResolvedBy,
            ResolvedAtMs = notification.ResolvedAtMs,
        };

        _logger.LogInformation(
            "Relaying approval resolution {ApprovalId} (kind={Kind}, decision={Decision}) to UI peers.",
            notification.Id, notification.Kind, notification.Decision);

        await _hub.BroadcastAsync(
            OpenClawRelayEnvelope.Create(EventTypes.OutputApprovalResolved, payload),
            excluding: null,
            cancellationToken).ConfigureAwait(false);
    }
}
