using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Abstractions;
using Seren.Application.OpenClaw.Notifications;
using Seren.Contracts.Events;
using Seren.Contracts.Events.Payloads;

namespace Seren.Application.OpenClaw.Handlers;

/// <summary>
/// Relays <see cref="AgentEventNotification"/> to connected UI peers as an
/// <c>output:agent:event</c> broadcast. Useful for UI affordances such as
/// "the assistant is calling tool X" or "tool Y finished with output".
/// </summary>
/// <remarks>
/// Agent streams upstream are verbose — we filter out streams the UI has no
/// use for ("delta" micro-updates inside a tool call) so the hub isn't
/// flooded. If a richer view is needed later the filter can be lifted.
/// </remarks>
public sealed class AgentEventToUiHandler : INotificationHandler<AgentEventNotification>
{
    private static readonly HashSet<string> RelayedStreams = new(StringComparer.Ordinal) { "tool", "item" };

    private readonly ISerenHub _hub;
    private readonly ILogger<AgentEventToUiHandler> _logger;

    public AgentEventToUiHandler(ISerenHub hub, ILogger<AgentEventToUiHandler> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async ValueTask Handle(
        AgentEventNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!RelayedStreams.Contains(notification.Stream))
        {
            _logger.LogDebug(
                "Skipping agent event stream={Stream} phase={Phase} for run {RunId}.",
                notification.Stream, notification.Phase ?? "<none>", notification.RunId);
            return;
        }

        var payload = new AgentEventPayload
        {
            RunId = notification.RunId,
            SessionKey = notification.SessionKey,
            Stream = notification.Stream,
            Phase = notification.Phase,
            Seq = notification.Seq,
            Data = notification.Data,
        };

        await _hub.BroadcastAsync(
            OpenClawRelayEnvelope.Create(EventTypes.OutputAgentEvent, payload),
            excluding: null,
            cancellationToken).ConfigureAwait(false);
    }
}
