using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Application.Chat;

namespace Seren.Application.Sessions;

/// <summary>
/// Reacts to <see cref="PeerAnnouncedNotification"/> by dispatching a
/// <see cref="LoadChatHistoryCommand"/> for that peer — pushing the most
/// recent transcript so the new client sees the conversation in progress
/// without any client-side persistence.
/// </summary>
public sealed class HydrateHistoryOnAnnouncedHandler
    : INotificationHandler<PeerAnnouncedNotification>
{
    /// <summary>Number of messages sent on first hydration.</summary>
    public const int InitialHydrationLimit = 50;

    private readonly ISender _sender;
    private readonly ILogger<HydrateHistoryOnAnnouncedHandler> _logger;

    public HydrateHistoryOnAnnouncedHandler(
        ISender sender,
        ILogger<HydrateHistoryOnAnnouncedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async ValueTask Handle(PeerAnnouncedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _logger.LogDebug(
            "Hydrating chat history for newly-announced peer {PeerId} (module={ModuleId})",
            notification.PeerId, notification.ModuleId);

        await _sender.Send(
            new LoadChatHistoryCommand(notification.PeerId, Before: null, Limit: InitialHydrationLimit),
            cancellationToken).ConfigureAwait(false);
    }
}
