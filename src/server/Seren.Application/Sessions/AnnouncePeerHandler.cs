using Mediator;
using Microsoft.Extensions.Logging;
using Seren.Contracts.Events.Payloads;
using Seren.Domain.Abstractions;
using Seren.Domain.ValueObjects;

namespace Seren.Application.Sessions;

/// <summary>
/// Handles <see cref="AnnouncePeerCommand"/> by registering the announced identity
/// on the peer in the <see cref="IPeerRegistry"/>, then publishing
/// <see cref="PeerAnnouncedNotification"/> so cross-cutting subscribers
/// (chat history hydration, presence broadcasts, …) can react.
/// </summary>
public sealed class AnnouncePeerHandler : IRequestHandler<AnnouncePeerCommand, AnnouncedPayload>
{
    private readonly IPeerRegistry _registry;
    private readonly IPublisher _publisher;
    private readonly ILogger<AnnouncePeerHandler> _logger;

    public AnnouncePeerHandler(
        IPeerRegistry registry,
        IPublisher publisher,
        ILogger<AnnouncePeerHandler> logger)
    {
        _registry = registry;
        _publisher = publisher;
        _logger = logger;
    }

    public async ValueTask<AnnouncedPayload> Handle(AnnouncePeerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_registry.TryGet(request.PeerId, out var peer) || peer is null)
        {
            throw new InvalidOperationException(
                $"Cannot announce unknown peer '{request.PeerId}'.");
        }

        var identity = new ModuleIdentity(
            Id: request.Payload.Identity.Id,
            PluginId: request.Payload.Identity.PluginId,
            Version: request.Payload.Identity.Version,
            Labels: request.Payload.Identity.Labels);

        var updated = peer.Announce(identity);
        _registry.Update(updated);

        _logger.LogInformation(
            "Peer {PeerId} announced as module {ModuleName} (plugin={PluginId}, instance={InstanceId})",
            request.PeerId, request.Payload.Name, identity.PluginId, identity.Id);

        // Notify subscribers that a peer is now ready to receive directed
        // events (history hydration, presence updates, …). Errors raised by
        // a subscriber must not break the announce response, so we swallow
        // and log — this matches the pattern used by OpenClawGatewayEventBridge.
        try
        {
            await _publisher.Publish(
                new PeerAnnouncedNotification(request.PeerId, identity.Id, identity.PluginId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Subscriber threw while handling PeerAnnouncedNotification for peer {PeerId}",
                request.PeerId);
        }

        return new AnnouncedPayload
        {
            Identity = request.Payload.Identity,
            Name = request.Payload.Name,
            Index = 0,
        };
    }
}
